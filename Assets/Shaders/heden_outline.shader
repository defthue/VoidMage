//=========================================================================================================================
// HEDEN — outline shell shader (inverted hull)
//
// Apply to duplicate "shell" meshes authored in Blender:
//   duplicate the object(s) to outline -> join duplicates (Ctrl+J) -> assign material named "outline"
//   -> Shade Smooth (required: the shell inflates along normals; smooth normals keep it sealed at corners)
//   -> export. Do NOT flip normals — this shader culls front faces instead.
//
// How it works: vertices get pushed outward along their (smooth) normals by Thickness, and only the
// inside of the inflated shell is rendered (front-face culling) — visible as a rim around the original.
//
// The hull must carry the SAME wind values as the mesh it wraps, or the ink line swings independently.
//=========================================================================================================================

HEADER
{
	Description = "HEDEN outline shell — inverted hull, thickness + color knobs";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_WIND, 0..1, "Wind" );
	Feature( F_WIND_MASK, 0..1, "Wind" );

	// Must match the F_DISTANCE_FADE setting of the surface material this shell wraps,
	// or the ink line outlives the mesh it was drawn around.
	Feature( F_DISTANCE_FADE, 0..1, "Distance Fade" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"

	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	// Screen-constant outline: push scales with distance so the line keeps the same on-screen width.
	// ScreenWidth ~2 = thin ink, ~4 = bold cartoon. Clamps guard the zoom extremes.
	float g_flOutlineScreenWidth < Default( 2.0 ); Range( 0.0, 8.0 ); UiGroup( "Outline,10/10" ); >;
	float g_flOutlineMinWorld < Default( 0.15 ); Range( 0.0, 2.0 ); UiGroup( "Outline,10/11" ); >;
	float g_flOutlineMaxWorld < Default( 1.0 ); Range( 0.0, 10.0 ); UiGroup( "Outline,10/12" ); >;

	// Interact thicken — the COLOR-FREE aim cue: the ink swells slightly while aimed
	// (same HedenOutlineWarm attribute the pixel side warms on). 0 = off; 0.4 means the
	// line grows 40% while aimed. Combine with a near-black InteractTint for a highlight
	// that says "alive" purely through weight.
	// These ink lines are tuned hairline-thin (ScreenWidth 0.5-0.8), so the swell must be
	// large in relative terms to be visible at all: 1.5 = the aimed line draws at 2.5x
	// its width, which on a hairline is finally a readable weight change.
	float g_flHedenOutlineWarmVs < Attribute( "HedenOutlineWarm" ); Default( 0.0 ); >;
	float g_flInteractThicken < Default( 1.5 ); Range( 0.0, 4.0 ); UiGroup( "Outline,10/43" ); >;

	StaticCombo( S_WIND, F_WIND, Sys( ALL ) );
	StaticCombo( S_WIND_MASK, F_WIND_MASK, Sys( ALL ) );

	#include "shaders/heden_wind.hlsl"
	#include "shaders/heden_landmark.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		#if ( S_WIND )
			// Respawn grow-in: must match heden_toon exactly or the shell detaches mid-grow.
			i.vPositionOs.xyz *= g_flHedenTreeGrow;
		#endif

		PixelInput o = ProcessVertex( i );

		#if ( S_WIND )
			o.vPositionWs.xyz += Wind_Offset( i.vPositionOs, o.vPositionWs.xyz, Wind_Mask( i.vColor ) );
		#endif

		float dist = distance( o.vPositionWs.xyz, g_vCameraPositionWs.xyz );

		// 0.0015 folds a typical FOV into the math so ScreenWidth reads roughly as pixels at 1080p.
		float push = clamp( g_flOutlineScreenWidth * dist * 0.0015, g_flOutlineMinWorld, g_flOutlineMaxWorld );
		push *= 1.0 + g_flHedenOutlineWarmVs * g_flInteractThicken;

		float3 pushedWs = o.vPositionWs.xyz + normalize( o.vNormalWs ) * push;
		o.vPositionWs.xyz = pushedWs;

		// Landmark far-plane squash, matching the surface this shell wraps — same rule as
		// F_DISTANCE_FADE above: if the shell doesn't squash with the mesh, the ink line
		// clips at 7500 while the tree it wraps renders on. No-op without the attribute.
		o.vPositionPs = Landmark_Project( pushedWs );

		return FinalizeVertex( o );
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "shaders/heden_darkness_zones.hlsl"

	// Scene fog. The surface shaders fade into the sky at the horizon via heden_toon_core;
	// without this the ink lines stay full-strength black on fully-fogged geometry and the
	// far clip re-appears as a square of outlines. Self-gates on the scene's fog state.
	#include "common/classes/Fog.hlsl"

	// Landmark fog variant — must match the surface shader, or the ink line fogs out at
	// 7000 while the landmark tree it wraps keeps fading to 9000 (or vice versa).
	#include "shaders/heden_landmark.hlsl"

	StaticCombo( S_DISTANCE_FADE, F_DISTANCE_FADE, Sys( ALL ) );
	#include "shaders/heden_distance_fade.hlsl"

	// Show only the inside of the inflated shell — the rim around the original object.
	RenderState( CullMode, FRONT );

	float3 g_vOutlineColor < UiType( Color ); Default3( 0.02, 0.02, 0.03 ); UiGroup( "Outline,10/20" ); >;

	// How much darkness zones (caves) swallow the ink line too. 1 = outlines fade to
	// black with the surfaces they wrap; 0 = outlines stay visible in the dark — the
	// silhouettes-in-the-void look. Tune per outline material.
	float g_flZoneDarknessResponse < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Outline,10/30" ); >;

	// Interact highlight: per-object attribute raised by the hover path while a harvestable
	// node is aimed (ResourceNodeComponent.SetHighlighted). Warms the ink toward the tint
	// with a slow breathe. Applied AFTER the darkness crush on purpose — this is aim
	// feedback, and the one place it matters most is a pitch-dark mine.
	// THE HEAVIER PEN (the chosen look): the aimed node's line swells bolder — weight is
	// the whole signal. The tint DEFAULTS TO THE INK COLOR so no color shift happens at
	// all (user judged even a 10% grey lift as wrong); pick a different tint per material
	// only if a color ever earns its way back.
	float g_flHedenOutlineWarm < Attribute( "HedenOutlineWarm" ); Default( 0.0 ); >;
	float3 g_vInteractTint < UiType( Color ); Default3( 0.02, 0.02, 0.03 ); UiGroup( "Outline,10/40" ); >;

	// The breathe: how fast and how deep the lift swells. Amount 0 = static (the chosen
	// default), raise it to make the line slowly pulse.
	float g_flInteractBreatheHz < Default( 1.0 ); Range( 0.0, 4.0 ); UiGroup( "Outline,10/41" ); >;
	float g_flInteractBreatheAmount < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Outline,10/42" ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 worldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

		// Before the depth early-out, matching the surface shader this shell wraps.
		DistanceFade_Clip( worldPos, i.vPositionSs.xy );

		#if ( S_MODE_DEPTH )
			return float4( 0.0, 0.0, 0.0, 1.0 );
		#endif

		float3 color = g_vOutlineColor;

		if ( g_flZoneDarknessResponse > 0.0 )
		{
			float zoneDark = Toon_ZoneDarkness( worldPos );

			// DarknessGroup-flagged objects: ink darkens by the 2D footprint too, matching
			// the surface pass (dungeon geometry is dark at any height).
			if ( g_flHedenObjectDarkness > 0.0 )
				zoneDark = max( zoneDark, g_flHedenObjectDarkness * Toon_ZoneFootprint( worldPos ) );

			color *= 1.0 - zoneDark * g_flZoneDarknessResponse;
		}

		// The interact warm rides on top of darkness (aim feedback must read in a dark
		// mine) but stays under fog — you only ever aim at nearby things.
		if ( g_flHedenOutlineWarm > 0.0 )
		{
			float breathe = 1.0 - g_flInteractBreatheAmount
				* ( 0.5 + 0.5 * sin( g_flTime * g_flInteractBreatheHz * 6.2831853 ) );
			color = lerp( color, g_vInteractTint, g_flHedenOutlineWarm * breathe );
		}

		// Fog last, same ordering as Toon_Shade: atmosphere sits in front of the ink,
		// it isn't part of the ink's colour. Landmark_Fog = Fog::Apply unless the object
		// carries HedenLandmarkFarZ.
		float3 inked = color;
		color = Landmark_Fog( worldPos, i.vPositionSs.xy, color );

		// The AIMED line punches through the atmosphere: at dusk and in weather the fog
		// tint turned the heavier pen blue, which read as the highlight breaking rather
		// than as haze. Feedback ink stays true black; everything unaimed keeps fogging.
		// You only ever aim at close things, so nothing distant can abuse this.
		color = lerp( color, inked, g_flHedenOutlineWarm );

		return float4( color, 1.0 );
	}
}
