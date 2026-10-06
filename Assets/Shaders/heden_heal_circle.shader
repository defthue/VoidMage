// HEDEN healing circle — a translucent ground disc drawn entirely in the pixel shader:
// a thick hot-green border whose radius wavers under ring-space gradient noise (the viny
// edge), broken tendril arcs creeping around it, and a domain-warped swirling fill inside.
// Everything hangs off polar coordinates of the quad's UVs; no textures are involved, so
// the vmat is as bare as hit_flash.vmat.
//
// Driven per-frame from C# (HealCircleVfx) through attributes:
//   g_flProgress  0..1  grow-in — the whole drawing expands out of the center
//   g_flFade      1..0  dissipate — global alpha envelope
//   g_flPulse     0..1  heal-beat flash — brightness spike, decays in code
//   g_flPreview   0/1   range telegraph while charging — border only, quiet, no bloom
//   g_vTintCore / g_vTintHot — the spell's palette (VfxCoreColor / VfxHotColor)
//
// Noise construction is heden_water's (sin-free hash, quintic gradient noise, rotated
// octaves with a domain warp); the depth feather is heden_beam's, so the disc melts into
// uneven terrain instead of slicing it.

HEADER
{
	Description = "HEDEN healing circle — procedural viny ring with swirling fill, unlit translucent";
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

	// Translucent, never depth-written: the disc lies on the ground and everything must
	// keep rendering through it. Culling off so it still reads from below a ledge.
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
	RenderState( DepthWriteEnable, false );
	RenderState( CullMode, NONE );

	// -- code-driven state ---------------------------------------------------------------

	float g_flProgress < Attribute( "g_flProgress" ); Default( 1.0 ); >;
	float g_flFade < Attribute( "g_flFade" ); Default( 1.0 ); >;
	float g_flPulse < Attribute( "g_flPulse" ); Default( 0.0 ); >;
	float g_flPreview < Attribute( "g_flPreview" ); Default( 0.0 ); >;

	// The circle's placement in WORLD units. The border is drawn where a pixel's world
	// distance from the center equals the radius — never derived from the quad's UVs or
	// scale, so the drawn ring and the gameplay radius cannot drift apart.
	float3 g_vCircleCenter < Attribute( "g_vCircleCenter" ); >;
	float g_flCircleRadius < Attribute( "g_flCircleRadius" ); Default( 180.0 ); >;
	float3 g_vTintCore < Attribute( "g_vTintCore" ); Default3( 0.2, 0.95, 0.35 ); >;
	float3 g_vTintHot < Attribute( "g_vTintHot" ); Default3( 0.85, 1.0, 0.85 ); >;

	// -- material tunables ---------------------------------------------------------------

	// Where the border sits in normalized radius, and how thick it is. The ring is the
	// spell's stated range, so it sits close to the quad edge with room for the vines to
	// wave outward without clipping.
	float g_flRingRadius < Default( 0.86 ); Range( 0.5, 0.98 ); UiGroup( "Ring,10/10" ); >;
	float g_flRingWidth < Default( 0.05 ); Range( 0.005, 0.2 ); UiGroup( "Ring,10/11" ); >;

	// A float, not a color picker — pickers clamp at 1 and the border must exceed 1 to
	// feed bloom (heden_beam's reasoning, verbatim).
	float g_flRingBrightness < Default( 1.6 ); Range( 1.0, 6.0 ); UiGroup( "Ring,10/12" ); >;

	// How far the noise bends the border radius (the viny waver) and how busy it is
	// around the circumference.
	float g_flVineAmount < Default( 0.035 ); Range( 0.0, 0.15 ); UiGroup( "Vines,10/20" ); >;
	float g_flVineScale < Default( 3.0 ); Range( 0.5, 12.0 ); UiGroup( "Vines,10/21" ); >;
	float g_flVineSpeed < Default( 0.35 ); Range( 0.0, 3.0 ); UiGroup( "Vines,10/22" ); >;

	// The swirling fill: feature scale, rotation rate, and how hard the spiral twists
	// toward the center.
	float g_flSwirlScale < Default( 2.6 ); Range( 0.5, 10.0 ); UiGroup( "Fill,10/30" ); >;
	float g_flSwirlSpeed < Default( 0.55 ); Range( 0.0, 4.0 ); UiGroup( "Fill,10/31" ); >;
	float g_flSwirlTwist < Default( 2.2 ); Range( 0.0, 8.0 ); UiGroup( "Fill,10/32" ); >;
	float g_flFillAlpha < Default( 0.28 ); Range( 0.0, 1.0 ); UiGroup( "Fill,10/33" ); >;

	float g_flRingAlpha < Default( 0.9 ); Range( 0.0, 1.0 ); UiGroup( "Ring,10/13" ); >;

	// World units over which the disc fades where it meets geometry (heden_beam's soft
	// landing) — hides the flat quad crossing bumpy terrain.
	float g_flDepthFeather < Default( 14.0 ); Range( 0.0, 64.0 ); UiGroup( "Blend,10/40" ); >;

	// -- heden_water's noise, prefixed for this shader -----------------------------------

	float Heal_Hash( float2 p )
	{
		float3 p3 = frac( float3( p.x, p.y, p.x ) * 0.1031 );
		p3 += dot( p3, p3.yzx + 33.33 );
		return frac( ( p3.x + p3.y ) * p3.z );
	}

	float2 Heal_Grad( float2 p )
	{
		float ang = Heal_Hash( p ) * 6.2831853;
		return float2( cos( ang ), sin( ang ) );
	}

	float Heal_GradNoise( float2 p )
	{
		float2 i = floor( p );
		float2 f = frac( p );
		float2 u = f * f * f * ( f * ( f * 6.0 - 15.0 ) + 10.0 );

		float a = dot( Heal_Grad( i ), f );
		float b = dot( Heal_Grad( i + float2( 1.0, 0.0 ) ), f - float2( 1.0, 0.0 ) );
		float c = dot( Heal_Grad( i + float2( 0.0, 1.0 ) ), f - float2( 0.0, 1.0 ) );
		float d = dot( Heal_Grad( i + float2( 1.0, 1.0 ) ), f - float2( 1.0, 1.0 ) );

		return lerp( lerp( a, b, u.x ), lerp( c, d, u.x ), u.y );
	}

	// Domain-warped rotated-octave fbm — heden_water's construction, the swirl's body.
	float Heal_PatchNoise( float2 p, float t )
	{
		const float2x2 rot = float2x2( 0.80, -0.60, 0.60, 0.80 );

		float2 warp = float2(
			Heal_GradNoise( p * 0.5 + float2( 7.3, 1.7 ) + t * 0.06 ),
			Heal_GradNoise( p * 0.5 + float2( 3.1, 9.2 ) - t * 0.05 ) );
		p += warp * 0.7;

		float n = Heal_GradNoise( p + float2( t * 0.35, t * 0.12 ) ) * 0.55;
		p = mul( rot, p ) * 2.1;
		n += Heal_GradNoise( p + float2( -t * 0.22, t * 0.07 ) ) * 0.30;
		p = mul( rot, p ) * 1.9;
		n += Heal_GradNoise( p + float2( t * 0.13, -t * 0.16 ) ) * 0.15;

		return saturate( n * 0.75 + 0.5 );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 worldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

		// The preview measures against the geometry BEHIND each pixel rather than the
		// quad itself. The telegraph hovers PreviewHeight above the feet (readability
		// over bumpy ground), and a ring drawn in that raised plane parallaxes across
		// the terrain as the camera moves — the circle looked like it changed size and
		// position with the view. Projecting through scene depth paints it ON the
		// ground at the exact gameplay radius from every angle; the quad is only
		// screen coverage, built oversize for the slack (BuildHealDisc's
		// coverageScale).
		float3 anchorPos = worldPos;
		if ( g_flPreview > 0.5 )
			anchorPos = Depth::GetWorldPosition( i.vPositionSs.xy );

		// Polar frame measured in the WORLD: normalized so the border lands at
		// g_flRingRadius exactly where a pixel sits g_flCircleRadius units from the
		// center — the same number the server heals with. The grow-in divides r rather
		// than scaling geometry, so the whole drawing — vines, swirl and all — expands
		// out of the center as one thing.
		float2 rel = anchorPos.xy - g_vCircleCenter.xy;
		float r = ( length( rel ) / max( g_flCircleRadius, 1.0 ) ) * g_flRingRadius / max( g_flProgress, 0.001 );
		float theta = atan2( rel.y, rel.x );

		// Ring-space sampling point: noise is read on the unit circle rather than on theta
		// directly, so there is no seam where the angle wraps.
		float2 ringPoint = float2( cos( theta ), sin( theta ) );
		float t = g_flTime;

		// -- the viny border -------------------------------------------------------------
		// Two waverings of the border radius: a broad slow one that makes the circle
		// breathe organically, and a finer, faster one riding it — together they read as
		// growth creeping around the edge rather than a drawn circle.
		float wob = Heal_GradNoise( ringPoint * g_flVineScale + float2( t * g_flVineSpeed, 0.0 ) ) * 0.6
			+ Heal_GradNoise( ringPoint * g_flVineScale * 2.3 + float2( -t * g_flVineSpeed * 0.7, 3.7 ) ) * 0.4;

		float ringR = g_flRingRadius + wob * g_flVineAmount;
		float ring = 1.0 - smoothstep( 0.0, g_flRingWidth, abs( r - ringR ) );

		// Tendrils: a thinner companion ring thrown further by the noise and cut by it too,
		// so only broken arcs survive — the pieces that look like vines reaching out.
		float wob2 = Heal_GradNoise( ringPoint * g_flVineScale * 1.7 + float2( t * g_flVineSpeed * 0.5, 8.2 ) );
		float ringR2 = g_flRingRadius - 0.05 + wob2 * g_flVineAmount * 3.0;
		float tendril = 1.0 - smoothstep( 0.0, g_flRingWidth * 0.5, abs( r - ringR2 ) );
		tendril *= smoothstep( 0.1, 0.5, wob2 + 0.5 );

		// -- the swirling fill -----------------------------------------------------------
		// The sampling angle rotates with time and twists with radius, so the warped fbm
		// is dragged into spiral wisps that turn around the center.
		float swirlAngle = theta + t * g_flSwirlSpeed - r * g_flSwirlTwist;
		float2 sp = float2( cos( swirlAngle ), sin( swirlAngle ) ) * r * g_flSwirlScale;
		float swirl = Heal_PatchNoise( sp, t );

		// Contrast-shaped so the wisps read as strands, then confined inside the border
		// with more presence near it than at the center — the pool looks deepest at its rim.
		swirl = smoothstep( 0.35, 0.8, swirl );
		float fillMask = 1.0 - smoothstep( ringR - 0.12, ringR, r );
		float fill = swirl * fillMask * ( 0.45 + 0.55 * saturate( r / max( ringR, 0.001 ) ) );

		// -- compose ---------------------------------------------------------------------
		float pulse = 1.0 + g_flPulse * 1.2;

		float3 color = g_vTintCore * fill;
		color = lerp( color, g_vTintCore * 1.2, tendril * 0.8 );
		color = lerp( color, g_vTintHot * g_flRingBrightness * pulse, ring );

		float alpha = fill * g_flFillAlpha
			+ tendril * g_flRingAlpha * 0.5
			+ ring * g_flRingAlpha;

		// Preview: the same circle reduced to a quiet range telegraph — border only, no
		// bloom-hot values, well under the cast's presence so the real thing still lands.
		if ( g_flPreview > 0.5 )
		{
			color = g_vTintCore * ( ring + tendril * 0.5 );
			alpha = ( ring + tendril * 0.4 ) * 0.28 + fill * 0.05;
		}

		alpha = saturate( alpha ) * saturate( g_flFade ) * ( 1.0 + g_flPulse * 0.3 );

		// Soft landing where the flat quad crosses terrain — heden_beam's feather. The
		// preview skips it: its drawing is already projected onto the surface, and
		// feathering by the quad's own hover distance would erase exactly the pixels
		// that landed.
		if ( g_flDepthFeather > 0 && g_flPreview < 0.5 )
		{
			float3 scenePos = Depth::GetWorldPosition( i.vPositionSs.xy );
			alpha *= clamp( distance( scenePos, worldPos ) / g_flDepthFeather, 0.0, 1.0 );
		}

		clip( alpha - 0.001 );

		return float4( color, saturate( alpha ) );
	}
}
