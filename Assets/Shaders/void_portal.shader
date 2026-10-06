//=========================================================================================================================
// VOID MAGE — portal surface: the sheet inside the Dark Fortress portal frame (SM_Prop_Portal_01_Portal).
//
// Fully procedural, so the sheet's UVs only have to run 0..1 across the quad (they do, front and back; measured
// 1.35 m x 2.27 m). Same black-hole language as heden_void_orb: a pitch dark heart with a thin hot horizon ring,
// spiral arms of violet light winding into it, and pale streaks falling in along the spiral. Three tricks:
//
//   * no seams: the arms are integer harmonics of the angle, the break-up noise is read in TWISTED cartesian
//     space (never in polar), and the streak cells are an integer count around the circle
//   * endless inward flow: the noise is sampled at two zoom phases half a cycle apart and cross-faded, so the
//     clouds shrink into the heart forever without ever popping
//   * depth: the vortex centre is parallax-shifted by the view direction (world tangents from screen derivatives,
//     so it does not care which way the UVs run), which puts the heart BEHIND the frame instead of on a poster
//
// Unlit and opaque: the portal is a light source. Pixel-snapped and posterised by default to sit with the
// PS2-on-a-TV look; both are material floats (0 turns either off).
//
// Attributes (runtime, from PortalVfx; the defaults animate on their own so ModelDoc and the editor show it alive):
//   PortalPhase    float   integrated swirl phase. Speed changes must never jump the pattern, so C# integrates
//   PortalFreeRun  float   1 = add g_flTime to the phase (nobody is driving it), 0 = PortalPhase only
//   PortalNotice   float   0..1 the player is near: brighter, the heart opens
//   PortalStall    float   0..1 held shut by a chase: dim, sick flicker
//   PortalColor    float3  the void colour (PortalVfx lerps it to the blocked red)
//=========================================================================================================================

HEADER
{
	Description = "Void Mage portal surface — procedural unlit void vortex";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
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
	#include "common/classes/Fog.hlsl"

	// -- code-driven state ---------------------------------------------------------------

	float g_flPortalPhase < Attribute( "PortalPhase" ); Default( 0.0 ); >;
	float g_flPortalFreeRun < Attribute( "PortalFreeRun" ); Default( 1.0 ); >;
	float g_flPortalNotice < Attribute( "PortalNotice" ); Default( 0.0 ); >;
	float g_flPortalStall < Attribute( "PortalStall" ); Default( 0.0 ); >;
	float3 g_vPortalColor < Attribute( "PortalColor" ); Default3( 0.55, 0.3, 0.95 ); >;

	// -- material tunables ---------------------------------------------------------------

	// Sheet height / width, so circles stay circles. Stretch then pulls the vortex into a tall ellipse to fill the door.
	float g_flAspect < Default( 1.68 ); Range( 0.5, 3.0 ); UiGroup( "Shape,10/10" ); >;
	float g_flStretch < Default( 1.3 ); Range( 1.0, 2.0 ); UiGroup( "Shape,10/11" ); >;
	// Pixels across the sheet's width (0 = smooth) and brightness steps (0 = smooth).
	float g_flPixels < Default( 72.0 ); Range( 0.0, 256.0 ); UiGroup( "Shape,10/12" ); >;
	float g_flBands < Default( 7.0 ); Range( 0.0, 16.0 ); UiGroup( "Shape,10/13" ); >;
	// How far behind the sheet the heart sits, world units.
	float g_flDepth < Default( 16.0 ); Range( 0.0, 60.0 ); UiGroup( "Shape,10/14" ); >;

	float g_flSpeed < Default( 1.0 ); Range( 0.0, 4.0 ); UiGroup( "Vortex,10/20" ); >;
	float g_flTwist < Default( 2.2 ); Range( 0.0, 6.0 ); UiGroup( "Vortex,10/21" ); >;
	float g_flArmSharpness < Default( 2.2 ); Range( 0.5, 6.0 ); UiGroup( "Vortex,10/22" ); >;
	float g_flNoiseScale < Default( 2.6 ); Range( 0.5, 8.0 ); UiGroup( "Vortex,10/23" ); >;
	float g_flNoiseWarp < Default( 2.4 ); Range( 0.0, 6.0 ); UiGroup( "Vortex,10/24" ); >;
	float g_flCoreRadius < Default( 0.16 ); Range( 0.02, 0.6 ); UiGroup( "Vortex,10/25" ); >;
	float g_flBrightness < Default( 1.6 ); Range( 0.2, 6.0 ); UiGroup( "Vortex,10/26" ); >;

	float g_flStreaks < Default( 1.0 ); Range( 0.0, 3.0 ); UiGroup( "Streaks,10/30" ); >;
	float g_flStreakCount < Default( 36.0 ); Range( 4.0, 96.0 ); UiGroup( "Streaks,10/31" ); >;

	float g_flRimGlow < Default( 0.5 ); Range( 0.0, 2.0 ); UiGroup( "Rim,10/40" ); >;
	float g_flRimWidth < Default( 0.14 ); Range( 0.01, 0.5 ); UiGroup( "Rim,10/41" ); >;

	// -- heden_water's noise, prefixed for this shader -----------------------------------

	float Portal_Hash( float2 p )
	{
		float3 p3 = frac( float3( p.x, p.y, p.x ) * 0.1031 );
		p3 += dot( p3, p3.yzx + 33.33 );
		return frac( ( p3.x + p3.y ) * p3.z );
	}

	float Portal_Noise( float2 p )
	{
		float2 i = floor( p );
		float2 f = frac( p );
		float2 u = f * f * ( 3.0 - 2.0 * f );

		float a = Portal_Hash( i );
		float b = Portal_Hash( i + float2( 1.0, 0.0 ) );
		float c = Portal_Hash( i + float2( 0.0, 1.0 ) );
		float d = Portal_Hash( i + float2( 1.0, 1.0 ) );

		return lerp( lerp( a, b, u.x ), lerp( c, d, u.x ), u.y );
	}

	float Portal_Fbm( float2 p )
	{
		return Portal_Noise( p ) * 0.57 + Portal_Noise( p * 2.13 + 7.1 ) * 0.29 + Portal_Noise( p * 4.31 + 3.7 ) * 0.14;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		#if ( S_MODE_DEPTH )
			return float4( 0.0, 0.0, 0.0, 1.0 );
		#endif

		float3 worldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 N = normalize( i.vNormalWs );
		float3 V = normalize( g_vCameraPositionWs.xyz - worldPos );
		float2 uv0 = i.vTextureCoords.xy;

		// World tangents of the UVs from screen derivatives (taken BEFORE the pixel snap, which has none).
		float3 dpx = ddx( worldPos );
		float3 dpy = ddy( worldPos );
		float2 dux = ddx( uv0 );
		float2 duy = ddy( uv0 );
		float det = dux.x * duy.y - dux.y * duy.x;
		float2 parallaxUv = float2( 0.0, 0.0 );
		if ( abs( det ) > 1e-12 )
		{
			float3 T = ( dpx * duy.y - dpy * dux.y ) / det;   // world units per unit u
			float3 B = ( dpy * dux.x - dpx * duy.x ) / det;   // world units per unit v
			float vn = max( abs( dot( V, N ) ), 0.35 );
			parallaxUv = float2( dot( V, T ) / max( dot( T, T ), 1e-4 ), dot( V, B ) / max( dot( B, B ), 1e-4 ) ) * ( g_flDepth / vn );
		}

		// The PS2 grid: square pixels, counted across the width.
		float2 uv = uv0;
		if ( g_flPixels >= 1.0 )
		{
			float2 grid = float2( g_flPixels, g_flPixels * g_flAspect );
			uv = ( floor( uv0 * grid ) + 0.5 ) / grid;
		}

		float notice = saturate( g_flPortalNotice );
		float stall = saturate( g_flPortalStall );
		float t = ( g_flPortalPhase + g_flTime * g_flPortalFreeRun ) * g_flSpeed;

		// Sheet space: x -1..1 across the width, y scaled by the aspect then squeezed so the vortex is a tall ellipse.
		float2 toP = float2( 2.0, 2.0 * g_flAspect / g_flStretch );
		float2 pFlat = ( uv - 0.5 ) * toP;
		float rFlat = length( pFlat );

		// The heart sits behind the sheet: what is near the centre shifts with the view, the rim does not.
		float2 p = pFlat - parallaxUv * toP * exp( -rFlat * 1.4 );
		float r = length( p );
		float a = atan2( p.y, p.x );
		float lr = log( max( r, 0.02 ) );

		// Break-up noise in twisted cartesian space, zooming inward forever (two phases, cross-faded).
		float twistAng = g_flTwist * lr + t * 0.35;
		float cs = cos( twistAng );
		float sn = sin( twistAng );
		float2 q = float2( p.x * cs - p.y * sn, p.x * sn + p.y * cs ) * g_flNoiseScale;
		float f0 = frac( t * 0.11 );
		float f1 = frac( t * 0.11 + 0.5 );
		float n = Portal_Fbm( q * exp2( f0 ) + 11.3 ) * ( 1.0 - abs( 2.0 * f0 - 1.0 ) )
				+ Portal_Fbm( q * exp2( f1 ) + 47.9 ) * ( 1.0 - abs( 2.0 * f1 - 1.0 ) );

		// Spiral arms: integer harmonics of the angle along a log spiral, bent by the noise.
		float warp = ( n - 0.5 ) * g_flNoiseWarp;
		float arm1 = 0.5 + 0.5 * sin( 3.0 * a + g_flTwist * 3.0 * lr + t * 1.3 + warp );
		float arm2 = 0.5 + 0.5 * sin( 5.0 * a + g_flTwist * 4.5 * lr + t * 2.1 - warp * 1.4 );
		float arms = pow( arm1, g_flArmSharpness ) * 0.7 + pow( arm2, g_flArmSharpness * 1.5 ) * 0.3;
		arms *= 0.55 + 0.9 * n;

		// Radial envelope: nothing in the heart, the arms at their brightest just outside it, thinning outward.
		float core = g_flCoreRadius * ( 1.0 + 0.45 * notice ) * ( 1.0 + 0.05 * sin( t * 1.7 ) );
		float outside = smoothstep( core, core * 2.4, r );
		float envelope = outside * ( 0.25 + 0.75 * exp( -max( r - core * 2.0, 0.0 ) * 1.5 ) );
		float v = arms * envelope;

		// The event horizon: a thin hot ring on the heart's edge, hot spots chasing around it.
		float ringW = core * 0.22;
		float ring = exp( -( ( r - core * 1.12 ) * ( r - core * 1.12 ) ) / ( ringW * ringW ) );
		ring *= 0.7 + 0.3 * sin( 2.0 * a - t * 2.9 ) * sin( 3.0 * a + t * 1.7 );
		v += ring * 0.9;

		// Streaks: pale sparks falling in along the spiral, one per angular cell, quickening as they drop.
		float aTwisted = a + g_flTwist * 0.6 * lr + t * 0.25;
		float cellCount = max( floor( g_flStreakCount ), 1.0 );   // whole cells only, or the atan2 branch shows as a seam
		float cellF = ( aTwisted / 6.2831853 + 0.5 ) * cellCount;
		float cellId = fmod( fmod( floor( cellF ), cellCount ) + cellCount, cellCount );
		float h1 = Portal_Hash( float2( cellId, 1.7 ) );
		float h2 = Portal_Hash( float2( cellId, 9.3 ) );
		float fall = frac( h1 + t * ( 0.10 + 0.14 * h2 ) );
		float rs = ( 1.0 - fall ) * ( 1.0 - fall ) * 1.5 + core;
		float across = 1.0 - abs( frac( cellF ) - 0.5 ) * 2.0;
		float len = 0.03 + 0.10 * fall;
		float streak = exp( -( ( r - rs ) * ( r - rs ) ) / ( len * len ) ) * pow( saturate( across ), 3.0 );
		streak *= smoothstep( 0.0, 0.15, fall ) * ( 1.0 - smoothstep( 0.85, 1.0, fall ) );
		v += streak * g_flStreaks * outside;

		// Where the void meets the stone: a breathing glow hugging the sheet's border (the frame's jagged
		// inner edge cuts into it, so it reads as light spilling round the rock).
		float2 edgeUv = min( uv, 1.0 - uv ) * float2( 1.0, g_flAspect );
		float edge = min( edgeUv.x, edgeUv.y );
		float rim = ( 1.0 - smoothstep( 0.0, g_flRimWidth, edge ) ) * ( 0.6 + 0.4 * n ) * ( 0.85 + 0.15 * sin( t * 1.1 ) );
		v += rim * g_flRimGlow;

		// Noticed: it wakes. Held shut: dim, with a sick stutter (the boss-eye flicker idiom).
		float flick = 0.55 + 0.45 * Portal_Hash( float2( floor( g_flTime * 13.0 ), 5.0 ) );
		v *= ( 1.0 + 0.6 * notice ) * lerp( 1.0, 0.6 * flick, stall );

		if ( g_flBands >= 1.0 )
			v = floor( v * g_flBands + 0.5 ) / g_flBands;

		// Near-black violet depths, the void colour, then a pale overbright that feeds bloom.
		float3 deep = g_vPortalColor * 0.02;
		float3 hot = lerp( g_vPortalColor, float3( 1.0, 0.95, 1.0 ), 0.75 ) * g_flBrightness;
		float3 color = lerp( deep, g_vPortalColor * 0.85, smoothstep( 0.0, 0.55, v ) );
		color = lerp( color, hot, smoothstep( 0.55, 1.25, v ) );

		color = Fog::Apply( worldPos, i.vPositionSs.xy, color );

		return float4( color, 1.0 );
	}
}
