// HEDEN boss telegraph — the attack's hit shape painted on the ground while the boss winds
// up: a hot border around the shape, and a fill that sweeps outward from the boss and
// completes exactly at the hit frame (C# drives g_flFill from the windup timer).
//
// Shapes are drawn in WORLD units against an origin + forward direction, never from the
// quad's UVs, so the drawn shape IS the server's hit test:
//   g_flShape  0 = cone (g_flHalfAngleCos), 1 = ring (inner..outer), 2 = line (±g_flHalfWidth)
//
// The drawing is projected through scene depth onto whatever is under each pixel (the
// heal preview's trick), so the quad only provides screen coverage: it hovers above the
// floor and the shape still lands flat on it from every camera angle.
//
// Attributes:
//   g_vOrigin, g_vForward   world-space boss position and locked facing (unit, planar)
//   g_flRangeMin/Max        ring inner/outer radius, cone radius, line length
//   g_flHalfAngleCos        cone half-angle cosine (-1 = full circle)
//   g_flHalfWidth           line half width
//   g_flFill                0..1 sweep progress toward the hit frame
//   g_flFade                1..0 alpha envelope after the hit
//   g_flPulse               0..1 impact flash
//   g_vColor                the attack's colour

HEADER
{
	Description = "HEDEN boss telegraph — ground-projected cone/ring/line with a timed fill";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		return FinalizeVertex( o );
	}
}

PS
{
	#define CUSTOM_MATERIAL_INPUTS
	#include "common/pixel.hlsl"

	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
	RenderState( DepthWriteEnable, false );
	RenderState( CullMode, NONE );

	float3 g_vOrigin < Attribute( "g_vOrigin" ); >;
	float3 g_vForward < Attribute( "g_vForward" ); Default3( 1.0, 0.0, 0.0 ); >;
	float g_flShape < Attribute( "g_flShape" ); Default( 0.0 ); >;
	float g_flRangeMin < Attribute( "g_flRangeMin" ); Default( 0.0 ); >;
	float g_flRangeMax < Attribute( "g_flRangeMax" ); Default( 150.0 ); >;
	float g_flHalfAngleCos < Attribute( "g_flHalfAngleCos" ); Default( 0.0 ); >;
	float g_flHalfWidth < Attribute( "g_flHalfWidth" ); Default( 60.0 ); >;
	float g_flFill < Attribute( "g_flFill" ); Default( 0.0 ); >;
	float g_flFade < Attribute( "g_flFade" ); Default( 1.0 ); >;
	float g_flPulse < Attribute( "g_flPulse" ); Default( 0.0 ); >;
	float3 g_vColor < Attribute( "g_vColor" ); Default3( 1.0, 0.25, 0.1 ); >;

	// Border thickness in world units, and the brightness push that feeds bloom.
	float g_flBorderWidth < Default( 6.0 ); Range( 1.0, 30.0 ); UiGroup( "Look,10/10" ); >;
	float g_flBorderBrightness < Default( 1.8 ); Range( 1.0, 6.0 ); UiGroup( "Look,10/11" ); >;
	float g_flFillAlpha < Default( 0.35 ); Range( 0.0, 1.0 ); UiGroup( "Look,10/12" ); >;
	float g_flBorderAlpha < Default( 0.85 ); Range( 0.0, 1.0 ); UiGroup( "Look,10/13" ); >;
	float g_flFillEdge < Default( 10.0 ); Range( 1.0, 60.0 ); UiGroup( "Look,10/14" ); >;

	// Signed distance to the shape's boundary in world units: negative inside.
	float Telegraph_Distance( float2 rel, out float along01 )
	{
		float2 fwd = normalize( g_vForward.xy );
		float2 right = float2( -fwd.y, fwd.x );
		float r = length( rel );
		float x = dot( rel, fwd );
		float y = dot( rel, right );

		int shape = (int)round( g_flShape );

		if ( shape == 2 )
		{
			// Line: a box from 0..RangeMax along fwd, ±HalfWidth across.
			along01 = saturate( x / max( g_flRangeMax, 1.0 ) );
			float dx = max( -x, x - g_flRangeMax );
			float dy = abs( y ) - g_flHalfWidth;
			return max( dx, dy );
		}

		// Ring / cone share the radial bounds.
		along01 = saturate( ( r - g_flRangeMin ) / max( g_flRangeMax - g_flRangeMin, 1.0 ) );
		float radial = max( g_flRangeMin - r, r - g_flRangeMax );

		if ( shape == 1 || g_flHalfAngleCos <= -0.999 )
			return radial;

		// Cone: angular bound as a distance to the edge ray, so the border is uniform.
		float cosA = r > 0.001 ? x / r : 1.0;
		float halfAngle = acos( clamp( g_flHalfAngleCos, -1.0, 1.0 ) );
		float angle = acos( clamp( cosA, -1.0, 1.0 ) );
		float angular = ( angle - halfAngle ) * max( r, 1.0 );

		return max( radial, angular );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		// Paint on the surface behind the pixel, not on the hovering quad.
		float3 groundPos = Depth::GetWorldPosition( i.vPositionSs.xy );
		float2 rel = groundPos.xy - g_vOrigin.xy;

		float along;
		float d = Telegraph_Distance( rel, along );

		float inside = 1.0 - smoothstep( -1.0, 1.0, d );
		float border = 1.0 - smoothstep( 0.0, g_flBorderWidth, abs( d ) );

		// The sweep: filled where the shape's own progress coordinate is behind the fill
		// front, with a soft leading edge. The front is what the player watches.
		float front = g_flFill;
		float fill = inside * ( 1.0 - smoothstep( front - g_flFillEdge / max( g_flRangeMax, 1.0 ), front, along ) );
		float frontLine = inside * ( 1.0 - smoothstep( 0.0, g_flFillEdge / max( g_flRangeMax, 1.0 ), abs( along - front ) ) ) * step( 0.001, front ) * step( front, 0.999 );

		float pulse = 1.0 + g_flPulse * 1.5;

		float3 color = g_vColor * 0.6;
		color = lerp( color, g_vColor, fill );
		color = lerp( color, g_vColor * g_flBorderBrightness * pulse, max( border, frontLine * 0.7 ) );

		float alpha = inside * g_flFillAlpha * ( 0.35 + 0.65 * fill )
			+ border * g_flBorderAlpha
			+ frontLine * 0.5;

		alpha = saturate( alpha ) * saturate( g_flFade ) * ( 1.0 + g_flPulse * 0.4 );

		clip( alpha - 0.002 );

		return float4( color, alpha );
	}
}
