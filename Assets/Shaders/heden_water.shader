//=========================================================================================================================
// HEDEN — cheap toon water (sea, pond, river, waterfall) — the Terryscape look at a fraction of the cost.
//
// One shader, three variants:
//   flat            pond / lake / sea — every pattern samples absolute WORLD XY, so adjacent meshes at the
//                   same height read as ONE body of water with zero coordination. UVs unused. Shape doesn't
//                   matter: drop an oversized plane into a terrain depression and the depth test hides the
//                   buried parts — the waterline follows the terrain contour, shore foam forms automatically.
//   flat + S_WAVES  adds VS sum-of-sines displacement (needs a subdivided grid — WaterPlane component).
//   S_WATERFALL     UV-space mode for closed Blender volumes: U = 0 at the lip -> 1 at the base, V wraps
//                   the perimeter. World-XY sampling degenerates on vertical sheets, hence the mode.
//
// Rivers are NOT a combo: flat mode + a streak layer gated by g_flFlowSpeed > 0 and vertex color G
// (flow strength, written by RiverSpline — fades to 0 at spline ends so junctions read as slack water).
//
// WHY THIS IS CHEAP (vs the old Terryscape water):
//   - ALL noise comes from materials/water_noise_01.png (tiling, baked by Editor > Heden/Water tool):
//     R patch fbm, G independent fbm, B sparkle field, A elongated streak field. 5 texture taps + 1 depth
//     read per pixel replaces ~40-96 per-pixel sin() calls (4th tap = golden-ratio de-tiling tap,
//     5th = the coarse patch-wobble warp field).
//   - S_TRANSLUCENT keeps it out of the opaque prepass and draws it after all opaques, so Depth::
//     reads the resolved scene beneath. CullMode NONE with a cheap backface early-out (flat deep
//     color underwater — no depth/noise work on backfaces), one clip() at the very end.
//   - Exclusion zones (heden_water_cut.hlsl): the FIRST thing the PS does is clip any pixel whose
//     world position sits inside one of up to 8 streamed "negative water" boxes (WaterExclusionZone
//     component) — that is how caves below sea level stop showing the ocean. ~10 ALU per slot.
//   - Nothing is pushed from C# per frame; every knob is a render ATTRIBUTE pushed on-change by the
//     WaterTuning component (per-water tuning in the inspector), g_flTime is engine-supplied. The
//     vmats only bind shader + noise texture + combos (waterfall geometry params stay vmat-side).
//
// Lighting: unlit + Fog::Apply (matches heden_beam / heden_ground_splat), plus a cheap sun-color tint
// (same lerp idiom as the toon core's SunTint) so water dims at dusk with the rest of the scene.
//
// Vertex color contract (LINEAR export if authored in Blender; RiverSpline writes it programmatically):
//   R = foam boost (optional accents), gated by g_flVertexFoamStrength which defaults 0 so stray data is inert
//   G = flow strength for the river streak layer (waterfall mode assumes full flow, no paint needed)
//=========================================================================================================================

HEADER
{
	Description = "HEDEN toon water — depth-banded translucent, shore foam, patches, sparkles, flow streaks, waterfall mode";
}

FEATURES
{
	#include "common/features.hlsl"
	// Don't enable both: waterfall mode samples UV space and has no use for surface waves.
	Feature( F_WATERFALL, 0..1, "Water" );
	Feature( F_WAVES, 0..1, "Water" );
}

MODES
{
	Forward();
	Depth();
}

COMMON
{
	#include "common/shared.hlsl"
	// Classifies the material as translucent: drawn AFTER all opaques (so the depth chain Depth::
	// reads is fully resolved), sorted back-to-front, excluded from the opaque depth prepass.
	// Without this the water lands in the opaque bucket and reads a stale depth buffer.
	#define S_TRANSLUCENT 1

	//-----------------------------------------------------------------------------------------------------------------
	// Wave field — declared HERE (not in VS) because BOTH stages need it: the VS displaces geometry
	// with it, and the PS re-evaluates it analytically for the shore foam contour. The foam line is
	// an isocontour of surface height, so feeding it the linearly-interpolated vertex height makes
	// it trace the wave grid's triangle edges — hard sawtooth teeth, which a shallow beach slope
	// amplifies enormously (small Z facet -> huge horizontal foam swing). Analytic per pixel = a
	// smooth curve at any grid resolution.
	//
	// Coordinates are PLANE-LOCAL (see WaterPlane): local units == world units, and the pattern
	// moves with the plane. Do NOT rebuild this from vPositionWs + the high-precision offset —
	// that reconstruction is camera-coupled in the VS stage and makes waves march at camera speed.
	// Keep in sync with any future CPU-side boat buoyancy sampling (also plane-local).
	//-----------------------------------------------------------------------------------------------------------------
	float g_flWaveHeight < Attribute( "WaveHeight" ); Default( 6.0 ); >;
	float g_flWaveLength < Attribute( "WaveLength" ); Default( 220.0 ); >;
	float g_flWaveSpeed < Attribute( "WaveSpeed" ); Default( 1.2 ); >;
	float g_flWaveDirectionDegrees < Attribute( "WaveDirectionDegrees" ); Default( 20.0 ); >;
	// 0 = the plain two-train field (regular corduroy). Up = a third off-axis train plus a long
	// swell envelope, so crest heights vary and sets roll through.
	float g_flWaveIrregularity < Attribute( "WaveIrregularity" ); Default( 0.5 ); >;

	// Travel direction of the swell — crests run PERPENDICULAR to this.
	float2 Water_WaveDir()
	{
		float ang = radians( g_flWaveDirectionDegrees );
		return float2( cos( ang ), sin( ang ) );
	}

	// Normalized wave field, roughly [-1, 1]. Returned as SHAPE (not height) so the PS can use it
	// for the crest highlight independently of WaveHeight — highlights work even on a flat plane.
	//
	// All trains are fanned only SLIGHTLY off the main axis on purpose. A train running
	// perpendicular to the primary (or far off-axis) turns the field into an interference lattice
	// whose peaks are isolated blobs, so crest highlights come out as ovals pointing the wrong way.
	// Keeping the fan narrow preserves long crest LINES square to the travel direction, while the
	// incommensurate wavelengths still make crest height and spacing vary along the wave.
	float Water_WaveShape( float2 wp, float t )
	{
		float ang = radians( g_flWaveDirectionDegrees );
		float2 d1 = Water_WaveDir();
		float k = 6.2831853 / max( g_flWaveLength, 1.0 );

		// Primary train — dominant, and the one that defines the crest lines.
		float p1 = dot( wp, d1 ) * k + t * g_flWaveSpeed;
		float s = sin( p1 );

		// +18° and -34°, both at incommensurate wavelengths and drifting at their own rates.
		float a2 = ang + 0.3142;
		float2 d2 = float2( cos( a2 ), sin( a2 ) );
		float p2 = dot( wp, d2 ) * ( k / 1.37 ) + t * g_flWaveSpeed * 0.83;

		float a3 = ang - 0.5934;
		float2 d3 = float2( cos( a3 ), sin( a3 ) );
		float p3 = dot( wp, d3 ) * ( k / 2.31 ) + t * g_flWaveSpeed * 0.61;

		float irr = g_flWaveIrregularity;
		s = lerp( s, s * 0.62 + 0.26 * sin( p2 ) + 0.16 * sin( p3 ), irr );

		// Long slow envelope: big and small sets rolling through instead of uniform swell.
		float setPhase = dot( wp, d1 ) * ( k / 6.7 ) + t * g_flWaveSpeed * 0.31;
		s *= lerp( 1.0, 0.55 + 0.45 * sin( setPhase ), irr );

		return s;
	}

	float Water_WaveOffset( float2 wp, float t )
	{
		return g_flWaveHeight * Water_WaveShape( wp, t );
	}

	//-----------------------------------------------------------------------------------------------------------------
	// Flow frame — makes a river's patches, waves, stretch and drift follow the channel.
	//
	// Rather than writing separate river pattern code, we ROTATE the sampling coordinate into a
	// flow-aligned frame. A rotation preserves the noise's spatial scale and keeps it world-
	// anchored (nothing swims), and every downstream effect inherits it for free: patch drift,
	// foam drift, wave crests, and the orbital surge (which is expressed via Water_WaveDir(), so
	// it stays consistent once the whole frame is rotated).
	//
	// RiverSpline packs the frame into vertex color BA as `dir * blend * 0.5 + 0.5`, so ONE
	// 2-component encoding carries both the downstream direction and how strongly to align.
	// Length 0 = world-aligned. WaterPlane writes (0.5, 0.5), so ponds take the identity path and
	// render bit-identically to before. This is deliberately not the vertex TANGENT: that only
	// reaches the PS when PS_INPUT_HAS_TANGENT_BASIS is set, whereas vVertexColor is unconditional
	// in both stages.
	//
	// The blend is what keeps a river/pond join invisible: alignment ramps back to world-space
	// before the meshes overlap, so both surfaces sample the same world XY pattern at the seam.
	//-----------------------------------------------------------------------------------------------------------------
	void Water_FlowAxes( float4 vc, out float2 fwd, out float2 side )
	{
		float2 v = vc.ba * 2.0 - 1.0;
		float blend = saturate( length( v ) );

		// Interpolate the ANGLE toward 0, not the direction vector. normalize(lerp(worldX, dir, b))
		// looks equivalent and is cheaper, but it is degenerate: a river flowing along world -X at
		// blend 0.5 lerps to the zero vector and normalize() returns NaN, which would poison every
		// pattern downstream. atan2 returns (-pi, pi], so scaling it by blend is already the
		// shortest path to world +X and stays continuous everywhere.
		float ang = ( blend > 1e-4 ) ? atan2( v.y, v.x ) : 0.0;
		float a = ang * blend;

		float s, c;
		sincos( a, s, c );
		fwd = float2( c, s );
		side = float2( -s, c );
	}

	// Express a point in the flow frame. Identity when fwd = (1,0), so ponds are untouched.
	float2 Water_ToFlow( float2 p, float2 fwd, float2 side )
	{
		return float2( dot( p, fwd ), dot( p, side ) );
	}
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"

	// Not in the default vertex input — must be requested explicitly.
	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	// vVertexColor (TEXCOORD4) already exists in the common pixel input — we just fill it.

	// xy = plane-local position, z = the wave offset the VS actually applied at this vertex.
	// The PS needs both to rebuild the smooth analytic surface height (see Water_WaveOffset).
	// TEXCOORD5 is free in common/pixelinput.hlsl.
	float3 vWaveData : TEXCOORD5;
};

VS
{
	#include "common/vertex.hlsl"

	StaticCombo( S_WAVES, F_WAVES, Sys( ALL ) );

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		o.vVertexColor = i.vColor;

		// Wave coordinate in the FLOW frame, so river crests run square to the channel while ponds
		// (blend 0) get the identity rotation and are unchanged. Rotating here rather than in the
		// PS is what keeps the two stages in agreement — the PS reads this same rotated coordinate
		// back out of vWaveData, so its analytic re-evaluation still matches what the VS displaced.
		float2 fwd, side;
		Water_FlowAxes( i.vColor, fwd, side );
		float2 wpWave = Water_ToFlow( i.vPositionOs.xy, fwd, side );

		float wave = 0.0;

		#if ( S_WAVES )
			wave = Water_WaveOffset( wpWave, g_flTime );
			o.vPositionWs.z += wave;
			o.vPositionPs = Position3WsToPs( o.vPositionWs.xyz );
		#endif

		// Plane-local XY interpolates exactly (the grid is planar), so the PS can evaluate the
		// wave analytically at the pixel; subtracting this faceted value removes the VS's
		// piecewise-linear approximation.
		o.vWaveData = float3( wpWave, wave );

		return FinalizeVertex( o );
	}
}

PS
{
	#define CUSTOM_MATERIAL_INPUTS
	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl" // DirectionalLightCB: g_DirectionalLightEnabled / Color
	#include "common/classes/Fog.hlsl"   // Fog::Apply self-gates on the scene's fog state
	#include "shaders/heden_water_cut.hlsl" // Water_IsCut: global "negative water" boxes

	StaticCombo( S_WATERFALL, F_WATERFALL, Sys( ALL ) );
	// Also needed in the PS: the shore foam contour re-evaluates the wave analytically.
	StaticCombo( S_WAVES, F_WAVES, Sys( ALL ) );

	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
	RenderState( DepthWriteEnable, false );   // redundant: S_TRANSLUCENT already defaults this false
	RenderState( CullMode, NONE );

	//---------------------------------------------------------------------------------------------------------------------
	// ONE WATER LAYER PER PIXEL.
	//
	// Two overlapping translucent waters composite twice and read as a dark patch. Depth tricks
	// can't fix it: translucents sort back-to-front, so the far surface draws first and the near
	// one still passes the depth test and blends over it. So instead the first water to reach a
	// pixel CLAIMS stencil bit 6; later waters fail the test and are discarded. Exactly one layer
	// per pixel. Which surface wins doesn't matter for same-height/same-look joins — every flat
	// pattern samples absolute world XY, so they are identical across the seam.
	//
	// REFERENCE-INDEPENDENT ON PURPOSE. A first attempt used Ref 0x40 + Func NOT_EQUAL + PassOp
	// REPLACE and made ALL water vanish: the state was applied, but the compare rejected every
	// pixel. The stencil reference is engine-managed dynamic state (rendersystemvulkan.dll exports
	// StencilReference / StencilCompareMask / StencilWriteMask, i.e. VK_DYNAMIC_STATE_*), so a
	// shader-declared StencilRef never reaches the comparison. This version therefore:
	//   - never sets StencilRef, and deliberately does NOT define STENCIL_ALREADY_SET, so the
	//     engine's own reference (3, from vr_common_ps_code.fxc) stands and these states simply
	//     override the parts we care about (last-wins; same idiom as sprite.shader).
	//   - tests with EQUAL against ReadMask 0x40. Any reference with bit 6 clear — 3 included, and
	//     any plausible engine value — makes this "pass wherever bit 6 is clear", i.e. wherever no
	//     water has drawn yet.
	//   - claims the bit with PassOp INVERT + WriteMask 0x40, which flips bit 6 from 0 to 1
	//     WITHOUT writing the reference, so the write is reference-independent too.
	//
	// Back* states are required because CullMode is NONE — back faces are tested separately, and
	// leaving them at the engine default would let a back face draw over an already-claimed pixel.
	//
	// Bit 6: bit 0 is the VR checkerboard skip mask and bit 1 marks dynamic objects; the engine's
	// WriteMask of 254 shows bit 0 is protected. Masks here are explicit so nothing else is touched.
	// Assumes the stencil buffer is cleared per frame — if water renders for one frame and then
	// disappears, that assumption is wrong and this cannot work as written.
	//---------------------------------------------------------------------------------------------------------------------
	RenderState( StencilEnable, true );
	RenderState( StencilReadMask,  0x40 );
	RenderState( StencilWriteMask, 0x40 );
	RenderState( StencilFunc,   EQUAL );
	RenderState( StencilPassOp, INVERT );
	RenderState( StencilFailOp, KEEP );
	RenderState( StencilDepthFailOp, KEEP );

	RenderState( BackStencilFunc,   EQUAL );
	RenderState( BackStencilPassOp, INVERT );
	RenderState( BackStencilFailOp, KEEP );
	RenderState( BackStencilDepthFailOp, KEEP );

	//---------------------------------------------------------------------------------------------------------------------
	// The one texture: baked tiling noise (Editor menu > Heden/Water: Generate Noise Texture).
	// RGBA8888 uncompressed on purpose — BC block artifacts show when smooth fields get thresholded
	// into hard bands. Linear, WRAP.
	//---------------------------------------------------------------------------------------------------------------------
	CreateInputTexture2D( WaterNoise, Linear, 8, "", "_noise", "Water,40/5", Default4( 0.5, 0.5, 0.5, 0.5 ) );
	Texture2D g_tNoise < Channel( RGBA, Box( WaterNoise ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( false ); >;
	SamplerState g_sNoise < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); >;

	//---------------------------------------------------------------------------------------------------------------------
	// Water colors + translucency.
	// ALL tunables below are ATTRIBUTE-DRIVEN: the WaterTuning component (Code/Client/WaterTuning.cs)
	// pushes them per renderer, so every water body tunes independently in the inspector. The vmats
	// only bind the shader, the noise texture, and the F_WAVES/F_WATERFALL combos. Defaults apply
	// when no WaterTuning is present (they match the old pond look).
	//---------------------------------------------------------------------------------------------------------------------
	// Per-water opt-out of the exclusion zones (WaterTuning.IgnoreExclusionZones): an underground
	// pool inside a cave box keeps rendering while the sea above it is cut.
	float g_flCutIgnore < Attribute( "CutIgnore" ); Default( 0.0 ); >;

	float3 g_vShallowColor < Attribute( "ShallowColor" ); Default3( 0.30, 0.78, 0.90 ); >;
	float3 g_vDeepColor < Attribute( "DeepColor" ); Default3( 0.10, 0.45, 0.75 ); >;
	float3 g_vFoamColor < Attribute( "FoamColor" ); Default3( 0.95, 0.98, 1.00 ); >;
	float g_flShallowOpacity < Attribute( "ShallowOpacity" ); Default( 0.55 ); >;
	float g_flDeepOpacity < Attribute( "DeepOpacity" ); Default( 0.85 ); >;

	// Water depth (world units) at which the color reaches full Deep, and the band count of the
	// quantized shallow->deep ramp — the Terryscape stepped look.
	float g_flDepthRange < Attribute( "DepthRange" ); Default( 48.0 ); >;
	float g_flDepthBands < Attribute( "DepthBands" ); Default( 3.0 ); >;

	// Cheap scene-light hook: tints toward the sun color exactly like the toon core's SunTint,
	// so water dims at dusk with everything else. No shadow taps.
	float g_flSunColorInfluence < Attribute( "SunColorInfluence" ); Default( 0.4 ); >;

	//---------------------------------------------------------------------------------------------------------------------
	// Patch layer — the Wind Waker element: drifting noise quantized into sink / hold / lift bands.
	// PatchScale = world units per texture repeat (the R field holds ~4 big cells per repeat).
	// De-tiled by summing a second tap of the same field at golden-ratio scale + 37° rotation.
	//---------------------------------------------------------------------------------------------------------------------
	float g_flPatchScale < Attribute( "PatchScale" ); Default( 384.0 ); >;
	float g_flPatchDriftSpeed < Attribute( "PatchDriftSpeed" ); Default( 6.0 ); >;
	float g_flPatchThresholdLow < Attribute( "PatchThresholdLow" ); Default( 0.42 ); >;
	float g_flPatchThresholdHigh < Attribute( "PatchThresholdHigh" ); Default( 0.66 ); >;
	float g_flPatchEdgeSoftness < Attribute( "PatchEdgeSoftness" ); Default( 0.08 ); >;
	float g_flPatchDarken < Attribute( "PatchDarken" ); Default( 0.45 ); >;
	float g_flPatchLighten < Attribute( "PatchLighten" ); Default( 0.55 ); >;
	// Orbital surge in WORLD UNITS: how far the pattern is carried forward under a crest and drawn
	// back in a trough. Push it past PatchDriftSpeed and the pattern visibly reverses each cycle.
	float g_flPatchSurge < Attribute( "PatchSurge" ); Default( 40.0 ); >;
	// Direction the patches drift, in degrees. On a POND this is measured from world +X; on a
	// RIVER the whole field is evaluated in the flow frame, so 0 = straight downstream and 90 =
	// straight across the channel. Default 20.3 reproduces the vector that used to be hardcoded.
	float g_flPatchDirectionDegrees < Attribute( "PatchDirectionDegrees" ); Default( 20.3 ); >;
	// Blob MORPH. The band is the threshold of two summed taps; when both taps share one velocity
	// the sum's outline is rigid and the blobs read as stamps carried along by the drift. This
	// pushes the two taps in equal-and-opposite directions PERPENDICULAR to the drift (world units
	// per second, relative): their average motion is still exactly the current, but hills of one
	// field approaching hills of the other make blobs bulge, bridge and pinch off. Perpendicular on
	// purpose — extra motion along the drift reads as a faster/slower current, not as shape change.
	// 0 = the old rigid sum, bit-identical.
	float g_flPatchMorphSpeed < Attribute( "PatchMorphSpeed" ); Default( 0.0 ); >;
	// Blob WOBBLE: a slow domain warp of the patch coordinate by a field 2.5x COARSER than the
	// patches (world-unit amplitude, and the warp field's own drift speed). Coarser-than-feature
	// warping bends each blob smoothly — squash, stretch, a little rotation — without touching the
	// toony edge quality; it is high-frequency warping that frays the bands (see the de-tiling
	// note below), and this is lowpass by construction. Costs one extra texture tap. 0 = no warp.
	float g_flPatchWobble < Attribute( "PatchWobble" ); Default( 0.0 ); >;
	float g_flPatchWobbleSpeed < Attribute( "PatchWobbleSpeed" ); Default( 0.0 ); >;

	//---------------------------------------------------------------------------------------------------------------------
	// Sparkles — product of the B field read at the patch tap and the foam tap (two different scales
	// and drifts), thresholded. Rides the two taps we already pay for.
	//---------------------------------------------------------------------------------------------------------------------
	float g_flSparkleStrength < Attribute( "SparkleStrength" ); Default( 0.35 ); >;
	float g_flSparkleThreshold < Attribute( "SparkleThreshold" ); Default( 0.55 ); >;

	//---------------------------------------------------------------------------------------------------------------------
	// Shore foam — where the scene sits within FoamDepth below the surface (depth-buffer read), with
	// a wobbled outer edge and lacy breakup. FoamNoiseScale = world units per texture repeat.
	//---------------------------------------------------------------------------------------------------------------------
	float g_flFoamDepth < Attribute( "FoamDepth" ); Default( 10.0 ); >;
	float g_flFoamNoiseScale < Attribute( "FoamNoiseScale" ); Default( 96.0 ); >;
	float g_flFoamEdgeWobble < Attribute( "FoamEdgeWobble" ); Default( 0.6 ); >;
	float g_flFoamDriftSpeed < Attribute( "FoamDriftSpeed" ); Default( 4.0 ); >;
	// Round holes punched in the band. 0 = clean solid Terryscape band (the default).
	float g_flFoamBreakup < Attribute( "FoamBreakup" ); Default( 0.0 ); >;
	float g_flFoamOpacity < Attribute( "FoamOpacity" ); Default( 0.95 ); >;
	// How much the swell moves the foam band. 0 = stable (bathymetry only); 1 = the waterline
	// surges with the waves, at the cost of foam appearing in open water over shallow seabed.
	float g_flFoamWaveInfluence < Attribute( "FoamWaveInfluence" ); Default( 0.25 ); >;

	// Optional painted foam accents (vertex color R). Defaults OFF so stray vertex data is inert.
	float g_flVertexFoamStrength < Attribute( "VertexFoamStrength" ); Default( 0.0 ); >;

	//---------------------------------------------------------------------------------------------------------------------
	// Crest highlight — tints the tops of the wave field toward FoamColor. Rides the wave shape the
	// foam pass already evaluates, so it is a few ALU. Independent of WaveHeight: it still works on
	// a flat plane, which is the cheap way to fake chop without paying for a dense grid.
	//---------------------------------------------------------------------------------------------------------------------
	float g_flCrestStrength < Attribute( "CrestStrength" ); Default( 0.25 ); >;
	// Higher = only the very tips light up.
	float g_flCrestThreshold < Attribute( "CrestThreshold" ); Default( 0.72 ); >;
	// Lower = harder, toonier highlight edge.
	float g_flCrestSoftness < Attribute( "CrestSoftness" ); Default( 0.08 ); >;

	//---------------------------------------------------------------------------------------------------------------------
	// Flow streaks (rivers) — active when FlowSpeed > 0, scaled by vertex color G. Rivers carry
	// U = downstream arc length in WORLD units, V = 0..1 across (written by RiverSpline).
	//---------------------------------------------------------------------------------------------------------------------
	float g_flFlowSpeed < Attribute( "FlowSpeed" ); Default( 0.0 ); >;          // world units / sec
	float g_flStreakLength < Attribute( "StreakLength" ); Default( 256.0 ); >;  // world units per texture repeat along flow
	float g_flStreakRows < Attribute( "StreakRows" ); Default( 6.0 ); >;        // lanes across the river
	float g_flStreakCoverage < Attribute( "StreakCoverage" ); Default( 0.35 ); >;
	float g_flStreakStrength < Attribute( "StreakStrength" ); Default( 0.7 ); >;

	#if ( S_WATERFALL )
		//-----------------------------------------------------------------------------------------------------------------
		// Waterfall mode — closed Blender volume, U = 0 lip -> 1 base, V = 0..1 around the perimeter.
		//-----------------------------------------------------------------------------------------------------------------
		float g_flFallLength < Default( 400.0 ); Range( 16.0, 4096.0 ); UiGroup( "Waterfall,58/10" ); >;  // world units lip->base
		float g_flFallWidth < Default( 200.0 ); Range( 16.0, 4096.0 ); UiGroup( "Waterfall,58/20" ); >;   // world units around perimeter
		float g_flFallSpeed < Default( 300.0 ); Range( 16.0, 2048.0 ); UiGroup( "Waterfall,58/30" ); >;   // world units / sec at the base
		// Fall time grows with sqrt(distance) — 0.5 makes constant-speed scrolling in the remapped
		// coordinate read as accelerating, stretching streaks. 1.0 turns the remap off.
		float g_flFallAccel < Default( 0.5 ); Range( 0.25, 1.0 ); UiGroup( "Waterfall,58/40" ); >;
		float g_flLipFraction < Default( 0.06 ); Range( 0.0, 0.3 ); UiGroup( "Waterfall,58/50" ); >;      // top foam band, fraction of U
		float g_flFrothFraction < Default( 0.18 ); Range( 0.0, 0.5 ); UiGroup( "Waterfall,58/60" ); >;    // bottom froth band, fraction of U
		float g_flContactFeather < Default( 24.0 ); Range( 0.0, 128.0 ); UiGroup( "Waterfall,58/70" ); >; // world units, foam near rocks/base
	#endif

	//---------------------------------------------------------------------------------------------------------------------

	// Tiny scalar hash for per-lane streak speed variation — one hash per pixel, not a noise field.
	float Water_HashLane( float lane )
	{
		float h = frac( lane * 0.1031 );
		h += h * ( h + 33.33 );
		return frac( h * lane * 1.7 + h );
	}

	// Streak foam mask from the elongated A field. coordX in "repeats", vAcross 0..1.
	float Water_Streaks( float coordX, float vAcross, float t, float flowStrength )
	{
		float lane = vAcross * g_flStreakRows;
		float laneHash = Water_HashLane( floor( lane ) );

		// Per-lane speed and phase variation so lanes never move in lockstep.
		float x = coordX * ( 0.85 + 0.3 * laneHash ) + laneHash * 7.31;
		float y = lane / 32.0; // the A field holds 32 cells across — one lane per cell

		float n = g_tNoise.Sample( g_sNoise, float2( x, y ) ).a;

		// Bank fade: streaks live mid-channel, not on the banks.
		float edgeFade = smoothstep( 0.02, 0.18, vAcross ) * smoothstep( 0.98, 0.82, vAcross );

		float threshold = 1.0 - g_flStreakCoverage;
		float streak = smoothstep( threshold, threshold + 0.12, n );
		return streak * edgeFade * flowStrength * g_flStreakStrength;
	}

	float4 MainPs( PixelInput i, bool bIsFrontFace : SV_IsFrontFace ) : SV_Target0
	{
		float3 worldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

		// Exclusion zones FIRST — before the backface path too, or a camera under the plane inside
		// a cave would still see the flat deep-blue underwater wall. A clipped pixel never runs
		// StencilPassOp, so it leaves the water stencil bit unclaimed for another water (an
		// underground pool that opted out) to draw there.
		if ( g_flCutIgnore < 0.5 && Water_IsCut( worldPos ) )
			clip( -1.0 );

		// Underwater: backfaces return flat deep color before any depth/noise work is done.
		if ( !bIsFrontFace )
			return float4( g_vDeepColor, g_flDeepOpacity );

		float3 scenePos = Depth::GetWorldPosition( i.vPositionSs.xy );
		float t = g_flTime;

		float2 uv = i.vTextureCoords.xy;
		float4 vc = i.vVertexColor;

		// Wave field, evaluated ANALYTICALLY at this pixel — never the VS's interpolated value,
		// which would make anything contoured off it trace the wave grid's triangle edges.
		// Computed once up here because the pattern surge, the crest highlight, and the depth
		// pass all read it.
		float waveShape = 0.0;
		#if ( S_WAVES )
			waveShape = Water_WaveShape( i.vWaveData.xy, t );
		#endif

		#if ( S_WATERFALL )
			// -------- WATERFALL: everything in UV space, proportioned by FallLength/FallWidth ----------
			float uT = pow( saturate( uv.x ), g_flFallAccel );
			float2 wp = float2( uT * g_flFallLength, uv.y * g_flFallWidth );
		#else
			// -------- FLAT: absolute world XY, rotated into the flow frame ------------------------------
			// Identity for ponds (vertex color BA = 0.5), so they are bit-identical to before. For a
			// river this rotates the whole pattern field downstream, which carries patches, foam,
			// sparkles (a product of the two taps) and the orbital surge along with it.
			float2 flowFwd, flowSide;
			Water_FlowAxes( vc, flowFwd, flowSide );
			float2 wp = Water_ToFlow( worldPos.xy, flowFwd, flowSide );
		#endif

		//-- the two shared noise taps (patch scale + foam scale, different drifts) --------------------------------------
		// De-tiling second patch tap: the same field at a golden-ratio scale, rotated ~37°. Two
		// smooth lattices at incommensurate scale/orientation never re-align, so their SUM has no
		// visible repeat — and it stays lowpass-smooth, so the thresholded bands keep clean toony
		// edges (warping by HIGH-frequency noise frayed them — don't go back to that; the wobble
		// below is a coarser-than-patch warp precisely so it does not). The rotation also rotates
		// the drift baked into the coordinate, decorrelating the two taps' motion for free.
		#define WATER_ROT37( p ) ( float2( (p).x * 0.7986 - (p).y * 0.6018, (p).x * 0.6018 + (p).y * 0.7986 ) * 0.618 )

		#if ( S_WATERFALL )
			// The body races downward; the slower "patch" variation rides along at a fraction of fall speed.
			float2 patchUv = ( wp - float2( t * g_flFallSpeed * 0.35, 0.0 ) ) / g_flPatchScale;
			float2 patchUv2 = WATER_ROT37( patchUv );
			float2 foamUv = ( wp - float2( t * g_flFallSpeed * 0.7, t * 2.0 ) ) / g_flFoamNoiseScale;
		#else
			// Orbital surge: a water particle circles as a swell passes — carried forward under the
			// crest, drawn back in the trough. Offsetting the pattern lookup along the travel
			// direction by the wave phase makes the surface detail lurch forward and ease back
			// instead of sliding at a constant rate, so it reads as riding the waves. Once the
			// surge outruns the steady drift the pattern genuinely reverses for part of the cycle.
			float2 surge = float2( 0.0, 0.0 );
			#if ( S_WAVES )
				surge = Water_WaveDir() * waveShape * g_flPatchSurge;
			#endif

			float patchAng = radians( g_flPatchDirectionDegrees );
			float2 patchDir = float2( cos( patchAng ), sin( patchAng ) );
			float2 patchSide = float2( -patchDir.y, patchDir.x );

			// Carrier motion shared by both patch taps: world position + orbital surge + steady drift.
			float2 wpPatch = wp + surge + t * g_flPatchDriftSpeed * patchDir;

			// Wobble: ONE tap of a field 2.5x coarser than the patches, drifting on its own vector
			// (back-and-across relative to the drift so it never lines up with the carrier motion).
			// R and G are independently seeded fbm fields, so a single sample yields both warp
			// axes. Sampled before the patch taps — this is the dependent read the wobble costs.
			float2 wobbleUv = ( wp + t * g_flPatchWobbleSpeed * ( -0.7 * patchDir - 0.7 * patchSide ) )
			                  / ( g_flPatchScale * 2.5 );
			float4 tapWobble = g_tNoise.Sample( g_sNoise, wobbleUv );
			wpPatch += ( tapWobble.rg - 0.5 ) * 2.0 * g_flPatchWobble;

			// Morph: tap 1 slides one way across the drift, tap 2 the other way. Field 1 moves world
			// -morph, field 2 +morph, so the sum's centroid still travels at exactly the drift while
			// its outline reshapes continuously. Applied BEFORE the de-tiling rotation so the
			// second field's world-space shift is the true mirror of the first's.
			float2 morph = patchSide * ( t * g_flPatchMorphSpeed * 0.5 );
			float2 patchUv = ( wpPatch + morph ) / g_flPatchScale;
			float2 patchUv2 = WATER_ROT37( ( wpPatch - morph ) / g_flPatchScale );

			float2 foamUv = ( wp + surge * 0.5 + t * g_flFoamDriftSpeed * float2( -0.31, 1.0 ) ) / g_flFoamNoiseScale;
		#endif

		float4 tapFoam = g_tNoise.Sample( g_sNoise, foamUv );
		float4 tapPatch = g_tNoise.Sample( g_sNoise, patchUv );
		// tapPatch2 also feeds the foam breakup holes and the sparkle product below, so those
		// inherit the morph/wobble motion — intended (sparkles flicker a touch more).
		float4 tapPatch2 = g_tNoise.Sample( g_sNoise, patchUv2 );

		//-- base color: quantized shallow -> deep by real water depth ---------------------------------------------------
		#if ( S_WATERFALL )
			// No meaningful vertical depth on a vertical sheet — shade the body by the patch field
			// instead so the falling water has tonal variation.
			float depthT = 0.65;
		#else
			// Flat (undisplaced) water level: vWaveData.z is exactly what the VS added, so this
			// recovers the plane itself — i.e. pure bathymetry, no swell.
			float flatZ = worldPos.z - i.vWaveData.z;

			// Wave-displaced surface from the analytic shape computed at the top of this function.
			float surfaceZ = flatZ + g_flWaveHeight * waveShape;

			// Sky guard: at far-plane pixels the reconstructed scenePos.z is meaningless (it can sit
			// above the surface, reading as depth 0 → a solid foam band along the horizon). Treat
			// sky-behind as max-deep water instead. Reverse-Z: the depth chain clears to 0 at far.
			float sceneDepthNorm = Depth::GetNormalized( i.vPositionSs.xy );
			bool bSkyBehind = ( sceneDepthNorm <= 0.0001 );

			// Colour banding reads bathymetry only, so the bands don't shimmer with the swell.
			float depthBelow = bSkyBehind ? g_flDepthRange : max( flatZ - scenePos.z, 0.0 );
			float depthT = saturate( depthBelow / g_flDepthRange );

			// Foam gets its own height. At influence 0 the band is stable (bathymetry only); at 1
			// the waterline surges with the swell. High values spray foam across OPEN WATER
			// wherever a trough dips within FoamDepth of the seabed — that is the tradeoff.
			float foamZ = lerp( flatZ, surfaceZ, g_flFoamWaveInfluence );
			float foamDepthBelow = bSkyBehind ? g_flDepthRange : max( foamZ - scenePos.z, 0.0 );
		#endif

		// Quantize into hard-ish bands (the stepped Terryscape ramp). Band centers, clamped to [0,1].
		float bands = max( g_flDepthBands, 1.0 );
		float bandT = min( floor( depthT * bands ), bands - 1.0 ) / max( bands - 1.0, 1.0 );

		float3 albedo = lerp( g_vShallowColor, g_vDeepColor, bandT );
		float alpha = lerp( g_flShallowOpacity, g_flDeepOpacity, bandT );

		//-- patch bands: sink / hold / lift on the drifting R field -----------------------------------------------------
		// Sum of the two incommensurate taps; summing halves the variance, so re-stretch around 0.5
		// to keep band coverage matching a single field (thresholds keep their meaning).
		float pn = saturate( ( ( tapPatch.r + tapPatch2.r ) * 0.5 - 0.5 ) * 1.5 + 0.5 );
		float ps = g_flPatchEdgeSoftness;
		float bandLow = smoothstep( g_flPatchThresholdLow - ps, g_flPatchThresholdLow + ps, pn );
		float bandHigh = smoothstep( g_flPatchThresholdHigh - ps, g_flPatchThresholdHigh + ps, pn );

		float3 colLow = albedo * ( 1.0 - g_flPatchDarken * 0.5 );
		float3 colHigh = lerp( albedo, g_vShallowColor, g_flPatchLighten );
		albedo = lerp( colLow, albedo, bandLow );
		albedo = lerp( albedo, colHigh, bandHigh );

		//-- sparkles: product of the two B reads we already have, thresholded -------------------------------------------
		float sparkle = smoothstep( g_flSparkleThreshold, g_flSparkleThreshold + 0.15, tapPatch2.b * tapFoam.b * 2.0 );
		albedo += sparkle * g_flSparkleStrength;

		//-- crest highlight: light up the tops of the wave field (shore foam still wins over it) ------------------------
		#if ( S_WATERFALL == 0 && S_WAVES )
			float crestT = saturate( waveShape * 0.5 + 0.5 );  // 0 at trough, 1 at crest
			float crest = smoothstep( g_flCrestThreshold, g_flCrestThreshold + max( g_flCrestSoftness, 0.001 ), crestT );
			albedo = lerp( albedo, g_vFoamColor, crest * g_flCrestStrength );
			// Only bites at high strength — keeps strong whitecaps from reading as ghostly.
			alpha = max( alpha, crest * g_flCrestStrength * g_flFoamOpacity );
		#endif

		//-- foam ----------------------------------------------------------------------------------------------------------
		float foam = 0.0;
		float wob = tapFoam.g * 2.0 - 1.0; // roughly [-1,1]

		#if ( S_WATERFALL )
			// Lip foam at the top edge, froth at the base, contact foam wherever the fall grazes geometry.
			float lip = 1.0 - smoothstep( g_flLipFraction * ( 0.6 + 0.5 * tapFoam.g ), g_flLipFraction * ( 1.2 + 0.5 * tapFoam.g ), uv.x );
			float froth = smoothstep( 1.0 - g_flFrothFraction * ( 1.0 + 0.6 * wob ), 1.0, uv.x );
			float contact = 1.0 - smoothstep( 0.0, max( g_flContactFeather, 0.001 ) * ( 0.6 + 0.5 * tapFoam.g ), distance( worldPos, scenePos ) );

			// Falling streaks: full flow, no vertex color needed in waterfall mode.
			float fallCoord = ( uT * g_flFallLength - t * g_flFallSpeed ) / g_flStreakLength;
			float streaks = Water_Streaks( fallCoord, uv.y, t, 1.0 );

			foam = max( max( lip, froth ), max( contact, streaks ) );
		#else
			// Shore foam, toon style (Terryscape's look): a SOLID band whose outer edge is shaped by
			// the smooth wobble field — that smooth curve IS the waterline shape. Near-step
			// transition keeps the edge crisp without shimmering.
			float foamDepth = g_flFoamDepth * ( 1.0 + wob * g_flFoamEdgeWobble );
			float shoreFoam = 1.0 - smoothstep( foamDepth * 0.92, foamDepth, foamDepthBelow );

			// Optional breakup: ROUND holes punched by an independent smooth field, held off near
			// the waterline so the shore edge stays solid. The hole strength is deliberately FLAT
			// across the outer band — ramping it toward the outer edge carves wedges out of the
			// boundary, which read as hard sawtooth teeth. Defaults to 0 (clean Terryscape band).
			float holeMask = smoothstep( 0.52, 0.60, tapPatch2.g );
			float holeGate = smoothstep( 0.0, foamDepth * 0.5, foamDepthBelow );
			shoreFoam = saturate( shoreFoam - holeMask * holeGate * g_flFoamBreakup );

			// River streaks: U = downstream world units, gated by painted flow strength (vColor G).
			float streaks = 0.0;
			if ( g_flFlowSpeed > 0.0 )
			{
				float flowCoord = ( uv.x - t * g_flFlowSpeed ) / g_flStreakLength;
				streaks = Water_Streaks( flowCoord, uv.y, t, vc.g );
			}

			foam = max( shoreFoam, streaks );
		#endif

		// Optional painted foam accents.
		foam = max( foam, vc.r * g_flVertexFoamStrength );

		albedo = lerp( albedo, g_vFoamColor, foam );
		alpha = max( alpha, foam * g_flFoamOpacity );

		//-- cheap scene light + fog ---------------------------------------------------------------------------------------
		float3 sunTint = g_DirectionalLightEnabled
			? lerp( float3( 1.0, 1.0, 1.0 ), g_DirectionalLightColor.rgb, g_flSunColorInfluence )
			: float3( 1.0, 1.0, 1.0 );
		float3 color = albedo * sunTint;

		color = Fog::Apply( worldPos, i.vPositionSs.xy, color );

		clip( alpha - 0.001 );
		return float4( color, saturate( alpha ) );
	}
}
