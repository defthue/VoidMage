// HEDEN toon AO — a fullscreen crevice-darkening pass, cel-banded to match heden_toon.
//
// Not shadows: no light knows about this and it knows about no light. Per pixel it asks
// the depth buffer "how crowded is the geometry around this point" (8 spiral taps,
// normal-weighted, range-checked) and darkens accordingly — alcoves, corners, gaps
// behind props. One flat cost for the whole screen regardless of light count, which is
// the whole reason it exists: 95 dungeon torches stay shadowless and cheap.
//
// The occlusion is quantized into cel bands (same construction as heden_toon's
// Toon_BandDynamic) so the darkening reads as drawn shading, not photographic smudge.
//
// Driven by ToonAoEffect (Code/Client/ToonAoEffect.cs) through attributes; runs after
// the opaque pass so translucent VFX (beams, circles, fire) are never darkened by it.

HEADER
{
	Description = "HEDEN toon AO — cel-banded screen-space crevice darkening";
}

MODES
{
	Default();
	Forward();
}

FEATURES
{
}

COMMON
{
	#include "postprocess/shared.hlsl"
}

struct VertexInput
{
	float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
	float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PixelInput
{
	float2 uv : TEXCOORD0;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif

	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

VS
{
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o;
		o.vPositionPs = float4( i.vPositionOs.xy, 0.0f, 1.0f );
		o.uv = i.vTexCoord;
		return o;
	}
}

PS
{
	#include "postprocess/common.hlsl"
	#include "common/classes/Depth.hlsl"

	Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;

	// How dark full occlusion gets (0 = off), the world-unit size of a "crevice", the
	// cel step count and their edge softness — mirrored from ToonAoEffect's properties.
	float g_flAoStrength < Attribute( "AoStrength" ); Default( 0.6 ); >;
	float g_flAoRadius < Attribute( "AoRadius" ); Default( 28.0 ); >;
	float g_flAoBands < Attribute( "AoBands" ); Default( 2.0 ); >;
	float g_flAoBandSoftness < Attribute( "AoBandSoftness" ); Default( 0.08 ); >;

	// Beyond this view distance the pass passes through untouched — sky, and geometry
	// far enough that crevice darkening would be sub-pixel noise anyway.
	static const float MaxAoDistance = 12000.0;

	// heden_toon's band quantizer, verbatim: cel steps cut at band midpoints.
	float BandAo( float value )
	{
		float bands = round( g_flAoBands );
		if ( bands < 1.0 )
			return saturate( value );

		float s = max( g_flAoBandSoftness, 0.001 );
		float t = saturate( value ) * bands;
		float band = floor( t );
		float edge = smoothstep( 0.5 - s, 0.5 + s, t - band );
		return saturate( ( band + edge ) / bands );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = CalculateViewportUv( i.vPositionSs.xy );
		float2 ss = uv * g_vViewportSize;

		float3 color = g_tColorBuffer.Sample( g_sBilinearMirror, uv ).rgb;

		float3 p = Depth::GetWorldPosition( ss );
		float viewDist = distance( g_vCameraPositionWs, p );

		if ( viewDist > MaxAoDistance )
			return float4( color, 1.0 );

		// Surface orientation from depth derivatives — no normal buffer needed. Flipped
		// toward the camera so mirrored derivative winding can't invert the test.
		float3 n = normalize( cross( ddy( p ), ddx( p ) ) );
		if ( dot( n, g_vCameraPositionWs - p ) < 0.0 )
			n = -n;

		// The sample pattern lives on the SURFACE, not the screen: a fixed spiral on the
		// pixel's tangent plane at world-unit offsets, each tap projected back into the
		// depth buffer. That makes the occlusion field a property of the geometry — it
		// stays put as the camera moves, stationary like the placed lights, and its
		// banded contours align with surfaces instead of screen pixels (the old
		// screen-space pattern was what slid around and quantized into square patches).
		// No per-pixel rotation, deliberately: this pass bands instead of blurring, and
		// a smooth continuous field is what lets the quantizer carve clean stable lines.
		float3 t1 = normalize( cross( n, abs( n.z ) < 0.9 ? float3( 0, 0, 1 ) : float3( 1, 0, 0 ) ) );
		float3 t2 = cross( n, t1 );

		float occlusion = 0.0;
		float total = 0.0;

		[unroll]
		for ( int k = 0; k < 16; k++ )
		{
			float angle = k * 2.3999632;
			float r = g_flAoRadius * sqrt( ( k + 1.0 ) / 16.0 );
			float3 sampleWs = p + ( t1 * cos( angle ) + t2 * sin( angle ) ) * r;

			// World → screen with the engine's own projection (common.fxc); taps that
			// leave the viewport are simply not counted rather than guessed at.
			float4 samplePs = Position3WsToPs( sampleWs );
			if ( samplePs.w <= 0.0 )
				continue;

			float2 sampleUv = samplePs.xy / samplePs.w * 0.5 + 0.5;
			sampleUv.y = 1.0 - sampleUv.y;
			if ( any( sampleUv < 0.0 ) || any( sampleUv > 1.0 ) )
				continue;

			total += 1.0;

			float3 q = Depth::GetWorldPosition( sampleUv * g_vViewportSize );
			float3 d = q - p;
			float dist = length( d );

			if ( dist < 0.001 )
				continue;

			// Occludes if the surface found there rises out of this pixel's hemisphere
			// (bias rejects flat-wall self-shadowing); the range check stops distant
			// background geometry from haloing foreground edges.
			float rangeFalloff = saturate( 1.0 - dist / ( g_flAoRadius * 2.5 ) );
			occlusion += saturate( dot( n, d / dist ) - 0.2 ) * rangeFalloff;
		}

		occlusion = saturate( occlusion / max( total, 4.0 ) * 2.6 );

		// Contrast shaping BEFORE the bands: crush the weak haze and saturate the deep
		// corners, so the darkening hugs actual creases tightly instead of washing
		// outward — then the quantizer turns what remains into clean cel lines.
		occlusion = smoothstep( 0.15, 0.7, occlusion );

		float ao = BandAo( occlusion ) * g_flAoStrength;
		color *= 1.0 - ao;

		return float4( color, 1.0 );
	}
}
