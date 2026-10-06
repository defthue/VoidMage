//=========================================================================================================================
// HEDEN — shared toon lighting core
//
// Included inside the PS block of heden_toon.shader and heden_foliage.shader.
// One source of truth for banding so the two shaders can never drift apart.
//
// Verified against sbox-public (July 2026):
//   - Sun lives in DirectionalLightCB (game/core/shaders/Shadows/DirectionalLightShadow.hlsl):
//       g_DirectionalLightEnabled, g_DirectionalLightColor, g_DirectionalLightDirection
//     Reached via: common/classes/Light.hlsl -> common/Shadow.hlsl -> Shadows/DirectionalLightShadow.hlsl
//   - Cast shadows: DirectionalLightShadow::GetVisibility( float3 worldPos, float4 vPositionSs )
//   - Ambient:      AmbientLight::From( float3 worldPos, float4 vPositionSs, float3 normalWs )
//   - Cluster lights (Light::Count/Light::From) still do NOT include the sun. Sun is manual, by design.
//   - Dynamic/baked scene lights: Light::Count( screenPos ) + Light::From( worldPos, screenPos, index )
//     (game/addons/base/Assets/shaders/common/classes/Light.hlsl) — per-pixel cluster-culled list,
//     Direction/Color/Attenuation/Visibility precomputed. Same pattern as the stock foliage.shader loop.
//=========================================================================================================================

#ifndef HEDEN_TOON_CORE_HLSL
#define HEDEN_TOON_CORE_HLSL

// Scene fog (gradient / cubemap / volumetric), applied at the end of Toon_Shade. Include
// is guarded engine-side; Fog::Apply self-gates on the scene's fog state, so scenes
// without fog components render bit-identically.
#include "common/classes/Fog.hlsl"

// Landmark fog variant (worldroot tree) — falls through to Fog::Apply for everything else.
#include "shaders/heden_landmark.hlsl"

//-------------------------------------------------------------------------------------------------------------------------
// Material knobs — Toon Lighting
//-------------------------------------------------------------------------------------------------------------------------

// Shadow side = albedo * this tint. Artists paint the FULLY LIT color; shadow is derived.
float3 g_vShadowTint < UiType( Color ); Default3( 0.55, 0.58, 0.72 ); UiGroup( "Toon Lighting,10/10" ); >;

// Where the lit/shadow terminator sits on half-Lambert (0.5 = geometric terminator).
float g_flBandThreshold < Default( 0.5 ); Range( 0.0, 1.0 ); UiGroup( "Toon Lighting,10/20" ); >;

// Band edge softness. 0.005 is the sweet spot found on smooth-shaded models.
// If a FLAT-shaded asset flickers per-face at grazing sun angles, raise this — that is the dial.
float g_flBandSoftness < Default( 0.005 ); Range( 0.0, 0.2 ); UiGroup( "Toon Lighting,10/30" ); >;

// Softness for the CAST-SHADOW band only. With crisp sun shadows on, the signal is clean —
// keep this small (0.01–0.05) for a sharp cel line. Raise it only if using the engine shadow path.
float g_flShadowBandSoftness < Default( 0.03 ); Range( 0.0, 0.5 ); UiGroup( "Toon Lighting,10/35" ); >;

// 0 = ignore the sun's color entirely (hex-accurate vertex paint). 1 = full sun tint on the lit band.
float g_flSunColorInfluence < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Toon Lighting,10/40" ); >;

// Environment/ambient lift on the shadow side ("shadow picks up sky color"). 0 = off (v1 default).
float g_flAmbientLift < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Toon Lighting,10/50" ); >;

// Minimum ambient on the shadow side, applied as a floor UNDER the engine-ambient lift above:
// shadow lift = max( engineAmbient * AmbientLift, this ). Keeps interiors from being pure shadow
// color even with AmbientLift at 0. Small on purpose — dynamic lights carry interiors.
float g_flAmbientFloor < Default( 0.05 ); Range( 0.0, 1.0 ); UiGroup( "Toon Lighting,10/55" ); >;

// Village day/night. The scene sets "VoidNight" (0 day, 1 night) once for everything it draws
// (Scene.RenderAttributes, VillageSky); a material answers it only as far as its Night Response
// says, so the dungeon and the characters (response 0, the default) never notice. At night a
// responding material trades its ambient floor, its sun influence and (ramp mode) its ramp for
// the night values below. Unset — the editor, a test scene — it is day.
float g_flVoidNight < Attribute( "VoidNight" ); Default( 0.0 ); >;
float g_flNightResponse < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Toon Night,12/10" ); >;
float g_flNightAmbientFloor < Default( 0.12 ); Range( 0.0, 1.0 ); UiGroup( "Toon Night,12/20" ); >;
float g_flNightSunColorInfluence < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Toon Night,12/30" ); >;

float Toon_Night() { return saturate( g_flVoidNight * g_flNightResponse ); }

// Darkness zones — per-pixel cave darkness (slots, Toon_OneZone, Toon_ZoneDarkness) —
// shared with heden_outline.shader so ink lines can darken with the surfaces they wrap.
#include "shaders/heden_darkness_zones.hlsl"

// Adopted style rig: final-color crush + triplanar albedo detail + band wobble, for every
// core consumer unconditionally (runtime knobs, no combo — see heden_style_detail.hlsl).
#include "shaders/heden_style_detail.hlsl"

//-------------------------------------------------------------------------------------------------------------------------
// Material knobs — Dynamic lights (Point/Spot components + baked indexed lights)
//
// Runtime uniforms, NOT static combos: none of these change resource declarations, so branching on
// defaults is free-ish and avoids multiplying shader compile permutations.
//-------------------------------------------------------------------------------------------------------------------------

// Master multiplier for dynamic light contribution. 0 skips the light loop entirely.
float g_flDynamicLightStrength < Default( 1.0 ); Range( 0.0, 4.0 ); UiGroup( "Dynamic Lights,30/10" ); >;

// Cel steps applied to each light's response (N·L * attenuation * shadow). 1 = single lit/unlit
// cut like the sun band, 2+ = posterized falloff rings, 0 = smooth (no banding, plain toon-less).
float g_flDynamicLightBands < Default( 2.0 ); Range( 0.0, 8.0 ); UiGroup( "Dynamic Lights,30/20" ); >;

// Edge softness of each cel step, as a fraction of one band's width. Raise if attenuation
// gradients alias into crawling edges on flat-shaded assets.
float g_flDynamicLightBandSoftness < Default( 0.05 ); Range( 0.001, 0.5 ); UiGroup( "Dynamic Lights,30/30" ); >;

// How bright the OUTERMOST band is relative to a normal band step. 1 = uniform steps (the
// classic look, and the default). Below 1 the first ring of a light's falloff becomes a
// faint glow-reach and the remaining bands renormalize above it — the dungeon's "4 bands
// but the outer one whispers" look.
float g_flDynamicLightOuterBandScale < Default( 1.0 ); Range( 0.05, 1.0 ); UiGroup( "Dynamic Lights,30/40" ); >;

// Edge softness for the outer band's ENTRY alone (where the light dies into darkness), as
// a fraction of that band's width. 0 falls back to the shared band softness — a crisp end
// like every other step. Raised toward 0.5 the light's outer limit melts out gradually
// instead of cutting, while the interior steps stay cel-crisp.
float g_flDynamicLightOuterSoftness < Default( 0.0 ); Range( 0.0, 0.5 ); UiGroup( "Dynamic Lights,30/41" ); >;

// How much of the light's shape comes from distance alone instead of surface angle.
// 0 = Lambert (N·L * falloff): each plane gets its own rings, and they jump at wall/floor
// corners because the wall is grazed while the floor is hit head-on. 1 = falloff only:
// bands become concentric shells around the flame, so the ring on the floor continues
// seamlessly up the wall. Backfaces are still cut to zero at any setting. Meant for
// architecture (walls/floor/ceiling); leave 0 on characters and props so they keep form.
float g_flDynamicLightWrap < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Dynamic Lights,30/50" ); >;

//-------------------------------------------------------------------------------------------------------------------------
// Material knobs — Highlight band. RUNTIME-gated on Strength (0 = off, the default), NOT a
// static combo: pure math, no resource changes, and the old S_HIGHLIGHT_BAND combo doubled
// every consumer's permutation count for a feature no material even used.
//-------------------------------------------------------------------------------------------------------------------------
// Half-Lambert value above which the highlight band kicks in (faces aimed at the sun).
float g_flHighlightThreshold < Default( 0.90 ); Range( 0.5, 1.0 ); UiGroup( "Highlight Band,20/10" ); >;

// 0 = highlight band OFF. Above-1 brightness lives here, NOT in a color picker — pickers
// clamp to 1.0 and the highlight vanishes. Turn on with values in the 1..3 range.
float g_flHighlightStrength < Default( 0.0 ); Range( 0.0, 3.0 ); UiGroup( "Highlight Band,20/20" ); >;

float3 g_vHighlightTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Highlight Band,20/30" ); >;

//-------------------------------------------------------------------------------------------------------------------------
// Ramp mode (S_USE_RAMP) — the band math, painted as a picture.
// A 256x1 strip: X = how lit (0 = away from sun / in shadow, 1 = facing sun), color = the shading there.
// Final color = albedo * ramp. Paint hard blocks for hard bands, gradients for soft ones,
// cool hues in the low end for painterly shadows. When on, it replaces threshold/softness/tint/highlight.
//-------------------------------------------------------------------------------------------------------------------------
#if ( S_USE_RAMP )
	CreateInputTexture2D( ToonRamp, Srgb, 8, "", "_ramp", "Toon Ramp,15/10", Default3( 1.0, 1.0, 1.0 ) );
	// RGBA8888 (uncompressed): block compression makes visible steps in smooth ramp gradients.
	Texture2D g_tToonRamp < Channel( RGB, Box( ToonRamp ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( true ); >;
	SamplerState g_sRampSampler < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;

	// The ramp a Night Response material fades to as the scene's VoidNight rises (see Toon_Night).
	CreateInputTexture2D( ToonRampNight, Srgb, 8, "", "_ramp", "Toon Night,12/40", Default3( 1.0, 1.0, 1.0 ) );
	Texture2D g_tToonRampNight < Channel( RGB, Box( ToonRampNight ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( true ); >;

	// Where on the ramp a cast shadow lands (a surface can face the sun but sit in a building's shadow —
	// this clamps it down to here). Should sit inside your ramp's dark band. Default 0.25.
	float g_flRampShadowPoint < Default( 0.25 ); Range( 0.0, 1.0 ); UiGroup( "Toon Ramp,15/20" ); >;

	float g_flRampBrightness < Default( 1.0 ); Range( 1.0, 3.0 ); UiGroup( "Toon Ramp,15/30" ); >;
#endif

//-------------------------------------------------------------------------------------------------------------------------
// Rim light — sun-weighted silhouette lift. RUNTIME-gated on Strength (0 = off, the default);
// the old S_RIM_LIGHT combo doubled permutations for pure math. Materials that used the
// feature carry an explicit strength (migration wrote 0.5 where they rode the old default).
//-------------------------------------------------------------------------------------------------------------------------
float3 g_vRimColor < UiType( Color ); Default3( 1.0, 0.97, 0.90 ); UiGroup( "Rim Light,25/10" ); >;
float g_flRimStrength < Default( 0.0 ); Range( 0.0, 3.0 ); UiGroup( "Rim Light,25/20" ); >;
float g_flRimThreshold < Default( 0.60 ); Range( 0.0, 1.0 ); UiGroup( "Rim Light,25/30" ); >;
float g_flRimSoftness < Default( 0.05 ); Range( 0.0, 0.5 ); UiGroup( "Rim Light,25/40" ); >;
float g_flRimSunWeight < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Rim Light,25/50" ); >;

//-------------------------------------------------------------------------------------------------------------------------
// Debug — flip this before reading any code. 0=off 1=albedo 2=normals 3=light bands 4=raw half-Lambert
// 5=sun visibility 6=cascade index 7=shadow sub-texel grid 8=world units per shadow texel
// 9=style detail texture raw (mid-gray where the material has no detail feature)
//-------------------------------------------------------------------------------------------------------------------------
float g_flDebugMode < Default( 0.0 ); Range( 0.0, 9.0 ); UiGroup( "Debug,90/10" ); >;

//-------------------------------------------------------------------------------------------------------------------------
// Crisp sun shadow sampling
//
// The engine's GetVisibility uses rotated-Poisson PCF with a per-pixel noise angle (ShadowNoise in
// ShadowFiltering.hlsl) plus a screen-space contact shadow mask. Both inject per-pixel noise that our
// banding amplifies into dots. This path instead takes a deterministic, gapless grid of hardware
// comparison taps (ShadowDepthPCFSampler = 2x2 bilinear PCF in silicon): zero noise, and an exact
// box filter that slides sub-texel-continuously so a moving caster's shadow edge translates
// smoothly instead of popping. Edge quality is then limited by shadow map texel density.
//
// 1.0 = crisp path (default). 0.0 = engine path (for A/B comparison). Lerps between them.
//-------------------------------------------------------------------------------------------------------------------------
float g_flCrispSunShadows < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Toon Lighting,10/37" ); >;

// Kernel TIER, expressed as a radius in shadow texels. Tap SPACING is always exactly 1 texel —
// see the uniformity note below — so this only selects how many taps wide the box is:
//   <=0.0 : 1 tap   (raw hardware 2x2, shows the texel staircase)
//   <=1.5 : 3x3     (exact 4x4-texel box)   <- legacy value 1.5 lands here, same cost as before
//   <=2.5 : 4x4     (exact 5x5-texel box)   <- recommended for ground/terrain receivers
//   else  : 5x5     (exact 6x6-texel box)
float g_flShadowSmoothing < Default( 1.5 ); Range( 0.0, 3.0 ); UiGroup( "Toon Lighting,10/38" ); >;

// Normal-offset shadows, measured in SHADOW TEXELS (not world units). Nudges the sample point
// along the surface normal before projecting, so surfaces never depth-test against themselves.
// Texel-relative is the whole point: a fixed world-space offset is many texels in a tight near
// cascade (peter-panning) and a fraction of a texel in the far one (acne) on the same frame.
// Auto-raised to the kernel half-width so the off-center taps can never out-reach the offset.
float g_flShadowNormalOffset < Default( 2.0 ); Range( 0.0, 8.0 ); UiGroup( "Toon Lighting,10/39" ); >;

// Ceiling on the resulting world-space offset, so a very coarse last cascade cannot detach
// shadows by metres. 100 units ~= 2.5 m at heden's 1 unit = 1 inch scale.
static const float HEDEN_MAX_NORMAL_OFFSET_WS = 100.0;

// Kernel half-extent in texels per tier — used to auto-size the normal offset above.
float Heden_KernelHalfTexels()
{
	if ( g_flShadowSmoothing <= 0.0 ) return 1.0;   // single tap, 2x2 footprint
	if ( g_flShadowSmoothing <= 1.5 ) return 2.0;   // 3x3 @1 -> exact 4x4 box
	if ( g_flShadowSmoothing <= 2.5 ) return 2.5;   // 4x4 @1 -> exact 5x5 box
	return 3.0;                                     // 5x5 @1 -> exact 6x6 box
}

//-------------------------------------------------------------------------------------------------------------------------
// WHY THE TAP SPACING IS EXACTLY 1.0 SHADOW TEXEL AND NOTHING ELSE
//
// ShadowDepthPCFSampler is COMPARISON_MIN_MAG_MIP_LINEAR, so one SampleCmpLevelZero is a hardware
// 2x2 bilinear PCF: it compares the 4 texels straddling the sample point and returns them weighted
// by the sub-texel fraction a. Lay K taps along one axis at spacing exactly 1 texel: tap k puts
// weight (1-a) on texel m+k and a on texel m+k+1. Summing over k, every INTERIOR texel receives
// a + (1-a) = 1 — exactly uniform, for ANY a — while the two end texels ramp with a and (1-a) and
// supply sub-texel continuous positioning. So a K-tap 1-texel grid is an exact box filter over
// (K+1) texels that still slides continuously with the receiver. Both properties die elsewhere:
//   spacing > 1 : texels between footprints get zero weight -> the occluded-fraction estimate is
//                 biased and NON-MONOTONE -> the thresholded edge stalls, then jumps backwards as
//                 a caster crosses a hole. The old 1.5 spacing sat here.
//   spacing < 1 : texels get a-dependent weights -> per-pixel bias -> shimmer along the edge.
//
// WHY THAT MATTERS FOR WIND: the shadow map is a BINARY occlusion raster — a leaf edge flips a
// texel on/off discretely. With an exact WxW box the occluded fraction moves in quanta of ~1/W^2
// and the ramp slope is ~1/W, so the thresholded 0.5 contour moves in steps of ~texelWorld/W world
// units. Widening W shrinks the step; denser cascades shrink texelWorld, which shrinks the step
// AND tightens corner rounding at the same time. Spend on cascades before taps.
//
// Note there is deliberately NO receiver-plane depth bias here. The old Heden_ReceiverPlaneDepthBias
// took ddx/ddy of the light-space position, but Toon_EvaluateSun early-outs before the shadow sample,
// so quads straddling the terminator have lanes that never wrote posLs; and FindCascade is per-lane,
// so quads straddling a cascade boundary mix positions from different projection matrices. Both give
// a garbage gradient whose damage scales with kernel radius. The engine's own 16-tap path likewise
// uses only normal offset + per-cascade constant bias.
//-------------------------------------------------------------------------------------------------------------------------
float Toon_SampleSunShadowCrisp( float3 worldPos, float3 normalWs,
                                 out int    dbgCascade,     // -1 = outside all cascades
                                 out float  dbgTexelWorld,  // world units per shadow texel
                                 out float2 dbgSubTexel )   // frac( posLs * mapSize ) — swim test
{
	dbgCascade    = -1;
	dbgTexelWorld = 0.0;
	dbgSubTexel   = float2( 0.0, 0.0 );

	if ( g_DirectionalLightCascadeCount == 0 )
		return 1.0;

	// Cascade from the RAW position. Texel size is only knowable once a cascade is chosen, and
	// selecting on the raw position also makes the choice independent of the shading normal,
	// which removes the seam where neighbouring pixels picked different cascades by normal alone.
	float3 posLsRaw;
	int cascade = FindCascade( worldPos, posLsRaw );
	if ( cascade < 0 )
		return 1.0;   // past the last cascade — matches the engine's GetVisibility

	float4x4 worldToShadow = g_DirectionalLightWorldToShadowViewMatrices[cascade];

	// World units per shadow texel for THIS cascade. Row 0 maps world -> shadow U, so |row0.xyz|
	// is "uv per world unit"; (uv per texel) / (uv per world unit) = world units per texel.
	// Same derivation the engine uses in DirectionalLightShadow::SampleCascade.
	float texelWorld = g_DirectionalLightInverseShadowMapSize
	                 / max( length( worldToShadow[0].xyz ), 1e-9 );

	float offsetTexels = ( g_flShadowNormalOffset <= 0.0 )
		? 0.0
		: max( g_flShadowNormalOffset, Heden_KernelHalfTexels() + 0.5 );

	float offsetWs = min( offsetTexels * texelWorld, HEDEN_MAX_NORMAL_OFFSET_WS );

	// Re-project the offset position through the SAME cascade matrix.
	float3 posLs = mul( worldToShadow, float4( worldPos + normalWs * offsetWs, 1.0 ) ).xyz;

	dbgCascade    = cascade;
	dbgTexelWorld = texelWorld;
	dbgSubTexel   = frac( posLs.xy / g_DirectionalLightInverseShadowMapSize );

	Texture2D shadowMap = Bindless::GetTexture2D( g_DirectionalLightShadowMapTextureIndex[cascade] );

	// g_DirectionalLightShadowBias is per-cascade AND already scaled by cascade size, so unlike
	// the normal offset it needs no correction here.
	float compareDepth = saturate( posLs.z + g_DirectionalLightShadowBias[cascade] );
	float2 texel = float2( g_DirectionalLightInverseShadowMapSize, g_DirectionalLightInverseShadowMapSize );

	if ( g_flShadowSmoothing <= 0.0 )
		return shadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler, posLs.xy, compareDepth );

	float shadow = 0.0;

	// Branches are on a material constant, so they are wave-uniform: one path per draw, no divergence.
	if ( g_flShadowSmoothing <= 1.5 )
	{
		// 3x3 @ 1 texel -> exact 4x4 box. 9 taps, same cost as the old holed kernel.
		[unroll]
		for ( int y = -1; y <= 1; y++ )
		{
			[unroll]
			for ( int x = -1; x <= 1; x++ )
				shadow += shadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler,
				          posLs.xy + float2( x, y ) * texel, compareDepth );
		}
		return shadow * ( 1.0 / 9.0 );
	}

	if ( g_flShadowSmoothing <= 2.5 )
	{
		// 4x4 @ 1 texel -> exact 5x5 box. 16 taps. Offsets +-0.5, +-1.5: no center tap needed,
		// the uniformity argument only requires unit spacing.
		[unroll]
		for ( int y = 0; y < 4; y++ )
		{
			[unroll]
			for ( int x = 0; x < 4; x++ )
				shadow += shadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler,
				          posLs.xy + ( float2( x, y ) - 1.5 ) * texel, compareDepth );
		}
		return shadow * ( 1.0 / 16.0 );
	}

	// 5x5 @ 1 texel -> exact 6x6 box. 25 taps.
	[unroll]
	for ( int y = -2; y <= 2; y++ )
	{
		[unroll]
		for ( int x = -2; x <= 2; x++ )
			shadow += shadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler,
			          posLs.xy + float2( x, y ) * texel, compareDepth );
	}
	return shadow * ( 1.0 / 25.0 );
}

//-------------------------------------------------------------------------------------------------------------------------
// Evaluation
//-------------------------------------------------------------------------------------------------------------------------

struct ToonLight
{
	float HalfLambert;   // N·L remapped to 0..1
	float SunVisibility; // cast-shadow visibility 0..1
	float LitBand;       // banded terminator (0 = shadow side, 1 = lit side)
	float ShadowBand;    // banded cast-shadow mask
	float Lit;           // LitBand * ShadowBand — the final toon light factor
	float3 SunTint;      // lerp( white, sun color, influence )
	float3 SunDir;

	// Diagnostics for the shadow debug modes (6/7/8). Kept on the struct so the three shaders
	// that call Toon_EvaluateSun need no signature changes of their own.
	float  DebugCascade;    // cascade index, -1 = outside all cascades
	float  DebugTexelWorld; // world units per shadow texel in that cascade
	float2 DebugSubTexel;   // sub-texel position — the cascade-swim test
};

float Toon_ShadowRelevanceFloor()
{
#if ( S_USE_RAMP )
	return g_flRampShadowPoint;
#else
	float s = max( g_flBandSoftness, 0.0001 );
	float floorValue = g_flBandThreshold - s;
	if ( g_flHighlightStrength > 0.0 )
		floorValue = min( floorValue, g_flHighlightThreshold - s );
	// Wobble can pull the terminator below the static threshold — widen the early-out
	// window by the max amplitude or wobbled pixels skip the shadow sample and band hard.
	floorValue -= g_flDetailWobble;
	return floorValue;
#endif
}

ToonLight Toon_EvaluateSun( float3 normalWs, float3 worldPos, float4 screenPos )
{
	ToonLight t;
	t.HalfLambert = 0.0;
	t.SunVisibility = 0.0;
	t.LitBand = 0.0;
	t.ShadowBand = 0.0;
	t.Lit = 0.0;
	t.SunTint = float3( 1.0, 1.0, 1.0 );
	t.SunDir = float3( 0.0, 0.0, 0.0 );
	t.DebugCascade = -1.0;
	t.DebugTexelWorld = 0.0;
	t.DebugSubTexel = float2( 0.0, 0.0 );

	if ( !g_DirectionalLightEnabled )
		return t;

	// Direction TO the sun — note the negation, g_DirectionalLightDirection points away.
	float3 sunDir = -normalize( g_DirectionalLightDirection.xyz );

	t.SunDir = sunDir;
	t.HalfLambert = dot( normalWs, sunDir ) * 0.5 + 0.5;
	t.SunTint = lerp( float3( 1.0, 1.0, 1.0 ), g_DirectionalLightColor.rgb, lerp( g_flSunColorInfluence, g_flNightSunColorInfluence, Toon_Night() ) );

	if ( t.HalfLambert <= Toon_ShadowRelevanceFloor() && g_flDebugMode < 0.5 )
		return t;

	// Crisp deterministic sample by default; engine's noisy PCF available for A/B via the knob.
	int shCascade; float shTexelWorld; float2 shSubTexel;
	float crispVis = Toon_SampleSunShadowCrisp( worldPos, normalWs, shCascade, shTexelWorld, shSubTexel );
	t.DebugCascade = (float)shCascade;
	t.DebugTexelWorld = shTexelWorld;
	t.DebugSubTexel = shSubTexel;

	if ( g_flCrispSunShadows >= 1.0 )
		t.SunVisibility = crispVis;
	else
		t.SunVisibility = lerp( DirectionalLightShadow::GetVisibility( worldPos, screenPos ), crispVis, g_flCrispSunShadows );

	float s = max( g_flBandSoftness, 0.0001 );
	// Style: the terminator wanders with world-space noise instead of cutting straight
	// (Detail_BandWobble self-gates on its knob — 0 costs nothing).
	float bandThreshold = g_flBandThreshold + Detail_BandWobble( worldPos, normalWs );
	t.LitBand = smoothstep( bandThreshold - s, bandThreshold + s, t.HalfLambert );

	// Band the soft PCF shadow too, so cast shadows get the same crisp toon edge as the terminator.
	// Uses its own (wider) softness so the engine's dithered shadow sampling doesn't turn into speckle.
	float ss = max( g_flShadowBandSoftness, 0.0001 );
	t.ShadowBand = smoothstep( 0.5 - ss, 0.5 + ss, t.SunVisibility );

	t.Lit = t.LitBand * t.ShadowBand;

	return t;
}

// Posterize a 0..1 light response into g_flDynamicLightBands cel steps with soft edges.
// Steps cut at each band's midpoint so response 0 stays 0 and response 1 stays 1.
//
// The outermost step is special-cased twice, both defaulting to "no change": its entry
// edge can take its own (softer) width so a light's outer limit melts into darkness
// instead of cutting, and its output level can be scaled faint — the bands above then
// renormalize so a full response still reaches exactly 1.
float Toon_BandDynamic( float response )
{
	float bands = round( g_flDynamicLightBands );
	if ( bands < 1.0 )
		return saturate( response ); // 0 = smooth falloff, no cel quantization

	float s = max( g_flDynamicLightBandSoftness, 0.001 );
	float t = saturate( response ) * bands;
	float band = floor( t );

	// The 0 -> first-band edge is the light's outer end; it alone may use the wider
	// softness. Every interior step keeps the shared crisp width.
	float sEdge = ( band < 0.5 && g_flDynamicLightOuterSoftness > 0.0 )
		? g_flDynamicLightOuterSoftness
		: s;

	float edge = smoothstep( 0.5 - sEdge, 0.5 + sEdge, t - band );
	float u = band + edge; // 0..bands, continuous

	// Map band units to output: the first band is worth OuterBandScale of a normal step,
	// the rest are worth 1 each, renormalized so u == bands still lands on 1.0 exactly.
	float first = g_flDynamicLightOuterBandScale;
	float levels = first + ( bands - 1.0 );
	float w = ( u <= 1.0 ) ? u * first : first + ( u - 1.0 );
	return saturate( w / max( levels, 0.001 ) );
}

// Toon_BandDynamic split into the faint outer reach and the crisp core bands, so the
// light loop can combine them differently: cores SUM (overlapping lit pools genuinely
// brighten each other), outer reaches MAX (two faint glows crossing merge into one glow
// instead of promoting each other a step). With OuterBandScale at 1 — every material
// that doesn't opt into the shaped outer band — everything lands in core and the result
// is bit-identical to the classic sum.
void Toon_BandDynamicSplit( float response, out float core, out float outer )
{
	core = 0.0;
	outer = 0.0;

	float bands = round( g_flDynamicLightBands );
	if ( bands < 1.0 )
	{
		core = saturate( response ); // 0 = smooth falloff, no cel quantization
		return;
	}

	if ( g_flDynamicLightOuterBandScale >= 0.999 )
	{
		core = Toon_BandDynamic( response );
		return;
	}

	float s = max( g_flDynamicLightBandSoftness, 0.001 );
	float t = saturate( response ) * bands;
	float band = floor( t );

	float sEdge = ( band < 0.5 && g_flDynamicLightOuterSoftness > 0.0 )
		? g_flDynamicLightOuterSoftness
		: s;

	float edge = smoothstep( 0.5 - sEdge, 0.5 + sEdge, t - band );
	float u = band + edge;

	float first = g_flDynamicLightOuterBandScale;
	float levels = max( first + ( bands - 1.0 ), 0.001 );

	outer = min( u, 1.0 ) * first / levels;
	core = max( u - 1.0, 0.0 ) / levels;
}

// Sum of all cluster-culled scene lights (dynamic + baked indexed) at this pixel, cel-banded.
// The cluster list never contains the sun (handled manually in Toon_EvaluateSun), but a directional
// light in the bins would double-count it — skipped as belt-and-braces.
float3 Toon_DynamicLights( float3 albedo, float3 normalWs, float3 worldPos, float4 screenPos )
{
	// Cores accumulate additively as always; the faint outer reaches combine by MAX, so
	// two torches' glow-edges crossing stay one quiet glow instead of stepping up a band.
	float3 coreSum = float3( 0.0, 0.0, 0.0 );
	float3 outerMax = float3( 0.0, 0.0, 0.0 );

	// Bounded by the engine's per-cluster bin capacity — this is the culled per-pixel list,
	// not the global scene light list.
	uint lightCount = Light::Count( screenPos );

	[loop]
	for ( uint li = 0; li < lightCount; li++ )
	{
		Light light = Light::From( worldPos, screenPos, li );

		if ( light.LightData.Type == LightType::LightTypeDirectional )
			continue;
		if ( !light.LightData.IsDiffuseEnabled() )
			continue;

		// Plain Lambert (not half) — point/spot lights must not lift backfaces. Wrap trades the
		// Lambert shape for a flat "faces the light" gate (short ramp so grazing surfaces fade
		// instead of popping), which leaves distance falloff as the only thing shaping the bands.
		// Attenuation folds distance falloff + spot cone; Visibility folds the light's shadow map.
		float nl = dot( normalWs, light.Direction );
		float facing = smoothstep( 0.0, 0.15, nl );
		float shape = lerp( saturate( nl ), facing, g_flDynamicLightWrap );
		float response = shape * light.Attenuation * light.Visibility;
		if ( response <= 0.0 )
			continue;

		float core, outer;
		Toon_BandDynamicSplit( response, core, outer );

		coreSum += light.Color * core;
		outerMax = max( outerMax, light.Color * outer );
	}

	return albedo * ( coreSum + outerMax ) * g_flDynamicLightStrength;
}

// albedo -> final toon-shaded color
float3 Toon_Shade( float3 albedo, ToonLight t, float3 normalWs, float3 worldPos, float4 screenPos )
{
	float3 result;

#if ( S_USE_RAMP )
	// How lit is this point: half-Lambert, clamped down by cast shadow.
	float shade = min( t.HalfLambert, lerp( g_flRampShadowPoint, 1.0, t.ShadowBand ) );

	// Style wobble, ramp flavor: the LOOKUP POINT wanders, so every band edge painted
	// in the ramp gets the noisy terminator. (Threshold-mode wobble lives in
	// Toon_EvaluateSun and never fires when the ramp replaces the threshold path.)
	shade = saturate( shade + Detail_BandWobble( worldPos, normalWs ) );

	float3 rampColor = g_tToonRamp.SampleLevel( g_sRampSampler, float2( shade, 0.5 ), 0 ).rgb;
	float night = Toon_Night();
	if ( night > 0.0 )
		rampColor = lerp( rampColor, g_tToonRampNight.SampleLevel( g_sRampSampler, float2( shade, 0.5 ), 0 ).rgb, night );

	// Sun color applies proportionally to how lit the point is, honoring the same influence knob.
	float3 sunTint = lerp( float3( 1.0, 1.0, 1.0 ), t.SunTint, shade );

	float rampBoost = lerp( 1.0, g_flRampBrightness, shade );

	result = albedo * rampColor * rampBoost * sunTint;

	float sunLitAmount = shade;
#else
	float3 shadowColor = albedo * g_vShadowTint;
	float3 litColor = albedo * t.SunTint;

	result = lerp( shadowColor, litColor, t.Lit );

	if ( g_flHighlightStrength > 0.0 )
	{
		float s = max( g_flBandSoftness, 0.0001 );
		float hiBand = smoothstep( g_flHighlightThreshold - s, g_flHighlightThreshold + s, t.HalfLambert );
		hiBand *= t.ShadowBand; // no highlights inside cast shadow
		float3 hiColor = saturate( albedo * g_vHighlightTint * g_flHighlightStrength ) * t.SunTint;
		result = lerp( result, hiColor, hiBand );
	}

	float sunLitAmount = t.Lit;
#endif

	// Ambient on the shadow side only, so the sun-lit look stays untouched: engine ambient
	// (env probe / probe volume / DDGI, whatever the scene runs) scaled by the lift knob,
	// with the flat floor as a guaranteed minimum so interiors never hit pure shadow color.
	{
		float ambientFloor = lerp( g_flAmbientFloor, g_flNightAmbientFloor, Toon_Night() );
		float3 ambient = float3( ambientFloor, ambientFloor, ambientFloor );
		if ( g_flAmbientLift > 0.0 )
			ambient = max( ambient, AmbientLight::From( worldPos, screenPos, normalWs ) * g_flAmbientLift );
		result += albedo * ambient * ( 1.0 - sunLitAmount );
	}

	// Darkness zones: inside a marked cave the UNLIT base — ramp/tint AND ambient floor,
	// whatever pack the material came from — crushes toward black per pixel. Applied
	// after ambient (a material's floor must not re-light a cave) and before dynamic
	// lights (torches and the staff light stay fully bright).
	//
	// How much direct sun survives is the zone's SunSpare (SoftPos.w). At 0 — the default —
	// the crush is a plain 1 - zoneDark: the zone itself roofs the cave, independent of
	// the sun's shadow map, whose cascade edges otherwise report "lit" and cut straight
	// bands of daylight through covered dungeons. Cave mouths catching real sunlight fade
	// in through the face softness instead. At 1, the spared measure is t.Lit — ACTUAL
	// sun (terminator band × cast shadow), NOT the ramp's sunLitAmount: the ramp clamps
	// its shadow side to RampShadowPoint (~0.25) by design, and sparing that floor left a
	// quarter of the base color glowing in covered dungeons even at darkness 1.
	{
		float zoneDark, sunSpare;
		Toon_ZoneDarkness2( worldPos, zoneDark, sunSpare );

		// Objects flagged by DarknessGroup (the dungeon's own geometry) also take the
		// zones' 2D footprint darkness — dark at ANY height, because the quarry digs into
		// the dungeon's airspace and no box top can separate them. Guarded so unflagged
		// objects render bit-identically.
		if ( g_flHedenObjectDarkness > 0.0 )
			zoneDark = max( zoneDark, g_flHedenObjectDarkness * Toon_ZoneFootprint( worldPos ) );

		if ( zoneDark > 0.0 )
			result *= 1.0 - zoneDark * ( 1.0 - t.Lit * sunSpare );
	}

	// Dynamic Point/Spot lights — additive on top, each banded through the cel quantizer.
	if ( g_flDynamicLightStrength > 0.0 )
		result += Toon_DynamicLights( albedo, normalWs, worldPos, screenPos );

	if ( g_flRimStrength > 0.0 )
	{
		float3 viewDir = normalize( g_vCameraPositionWs.xyz - worldPos );
		float fresnel = 1.0 - saturate( dot( normalWs, viewDir ) );

		float rs = max( g_flRimSoftness, 0.0001 );
		float rim = smoothstep( g_flRimThreshold - rs, g_flRimThreshold + rs, fresnel );

		float sunFacing = saturate( dot( normalWs, t.SunDir ) );
		rim *= lerp( 1.0, sunFacing, g_flRimSunWeight );
		rim *= t.ShadowBand;

		result += g_vRimColor * ( rim * g_flRimStrength );
	}

	// Style test crush: posterize the final shaded color — detail texture, cel bands, darkness
	// and torch pools all shatter into the same flat islands. BEFORE fog on purpose: fog
	// gradients stay smooth so "should fog crush too?" stays a separate future experiment.
	result = Crush_Apply( result );

	// Scene fog last, over everything including zone darkness and torch light — fog is
	// atmosphere in front of the surface, not part of its shading. Landmark_Fog is
	// Fog::Apply verbatim unless the object carries HedenLandmarkFarZ.
	result = Landmark_Fog( worldPos, screenPos.xy, result );

	return result;
}

// Returns true if it wrote a debug color (caller should return it immediately).
bool Toon_Debug( float3 albedo, ToonLight t, float3 normalWs, float3 worldPos, out float4 debugColor )
{
	debugColor = float4( 0, 0, 0, 1 );
	int mode = (int)round( g_flDebugMode );

	if ( mode == 1 )      debugColor = float4( albedo, 1.0 );                    // albedo only — black model? vertex paint is missing
	else if ( mode == 2 ) debugColor = float4( normalWs * 0.5 + 0.5, 1.0 );      // normals as RGB — patchwork? Shade Smooth in Blender
	else if ( mode == 3 ) debugColor = float4( t.Lit.xxx, 1.0 );                 // final banded light factor
	else if ( mode == 4 ) debugColor = float4( t.HalfLambert.xxx, 1.0 );         // raw half-Lambert, pre-banding
	else if ( mode == 5 ) debugColor = float4( t.SunVisibility.xxx, 1.0 );       // cast-shadow visibility — spiky? shadow map resolution, not shader math
	else if ( mode == 6 )                                                        // cascade index as color: R,G,B,Y = 0,1,2,3; black = outside all cascades
	{
		int c = (int)round( t.DebugCascade );
		float3 cc = ( c == 0 ) ? float3( 1, 0, 0 )
		          : ( c == 1 ) ? float3( 0, 1, 0 )
		          : ( c == 2 ) ? float3( 0, 0, 1 )
		          : ( c == 3 ) ? float3( 1, 1, 0 ) : float3( 0, 0, 0 );
		debugColor = float4( cc, 1.0 );
	}
	// Sub-texel grid — THE cascade-swim test. Stand still on flat ground and strafe WITHOUT
	// rotating: pattern glued to the world = cascades are texel-snapped, camera motion is not a
	// cause of shadow crawl. Pattern sliding across static geometry = not snapped, and walking
	// alone will crawl the shadow edge no matter what the filtering does.
	else if ( mode == 7 ) debugColor = float4( t.DebugSubTexel, 0.0, 1.0 );
	// World units per shadow texel, normalized to 8. Black = dense (good), white = coarse.
	// Read this per cascade instead of trusting any estimate of the shadow map resolution.
	else if ( mode == 8 ) debugColor = float4( saturate( t.DebugTexelWorld / 8.0 ).xxx, 1.0 );
	// Style detail texture, raw triplanar sample — is the texture wrong, or the crush?
	else if ( mode == 9 )
		debugColor = float4( Detail_SampleTriplanar( worldPos, normalWs, g_flDetailScale ).xxx, 1.0 );
	else return false;

	return true;
}

#endif // HEDEN_TOON_CORE_HLSL
