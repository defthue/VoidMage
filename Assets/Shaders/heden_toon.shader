//=========================================================================================================================
// HEDEN — toon object shader (tiles, buildings, props)
//
// Albedo comes from EITHER vertex color (default) OR a texture — clean either/or via F_USE_TEXTURE,
// never a multiply hybrid (sRGB default-texture interactions made hybrids darken unpredictably).
//
// Vertex paint represents the FULLY LIT color; shadow/highlight are derived from it.
// Blender export: Vertex Colors = LINEAR (sRGB double-converts and everything arrives too light).
// Models without vertex paint render black in vertex-color mode — flip Debug to 1 (albedo) to confirm.
// Assign the material ON THE MODEL (model editor), not via renderer Material Override.
//=========================================================================================================================

HEADER
{
	Description = "HEDEN toon object shader — 2-band + optional highlight, vertex color or texture albedo";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_USE_TEXTURE, 0..1, "Albedo" );

	Feature( F_USE_RAMP, 0..1, "Toon Lighting" );
	Feature( F_WIND, 0..1, "Wind" );
	Feature( F_WIND_MASK, 0..1, "Wind" );

	// Enable on materials drawn by InstancedPropBatch — it dissolves them out before the
	// batch's hard MaxDrawDistance cull. Costs a discard (late depth-stencil), so leave it
	// off for characters and anything else that never reaches a cull distance.
	Feature( F_DISTANCE_FADE, 0..1, "Distance Fade" );

	// PERMUTATION BUDGET: only combos that change RESOURCE declarations (textures) or isolate
	// a real GPU cost (distance-fade's discard) get to be features. Everything else — rim,
	// highlight, rune, object-tint, the style-detail rig — is a runtime knob gated on its own
	// value: wave-uniform branch, near-free, and it doesn't double the compile. The old
	// 8-feature version compiled 384 PS permutations (11.5MB, ~90s); this one compiles 8.
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

	// Not in the default vertex input — must be requested explicitly.
	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	// vVertexColor (TEXCOORD4) already exists in the common pixel input — we just fill it.

	// The per-object Tint (SceneObject.ColorTint). ProcessVertex delivers it in vVertexColor,
	// which MainVs then overwrites with the real vertex colour for the wind mask, so it is
	// carried separately. 14 is free of the common pixel input's semantics.
	float4 vObjectTint : TEXCOORD14;

	// Object-space frame for the style-detail rig (heden_style_detail.hlsl): materials with
	// DetailObjectSpace on sample the noise here so the pattern rides moving objects
	// (weapons, debris) instead of swimming through a world-glued projection.
	float3 vDetailPosOs : TEXCOORD13;
	float3 vDetailNormalOs : TEXCOORD15;
};

VS
{
	#include "common/vertex.hlsl"

	StaticCombo( S_WIND, F_WIND, Sys( ALL ) );
	StaticCombo( S_WIND_MASK, F_WIND_MASK, Sys( ALL ) );

	#include "shaders/heden_wind.hlsl"
	#include "shaders/heden_landmark.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		#if ( S_WIND )
			// Respawn grow-in: object-space scale about the origin, before the world transform.
			i.vPositionOs.xyz *= g_flHedenTreeGrow;
		#endif

		PixelInput o = ProcessVertex( i );
		o.vObjectTint = o.vVertexColor;
		o.vVertexColor = i.vColor;

		// Object-space frame for the style-detail rig. Position taken AFTER the grow-in
		// scale so the pattern rides a respawning tree instead of sliding through it.
		o.vDetailPosOs = i.vPositionOs.xyz;
		{
			float3 detailNormalOs; float4 detailTangentOs;
			VS_DecodeObjectSpaceNormalAndTangent( i, detailNormalOs, detailTangentOs );
			o.vDetailNormalOs = detailNormalOs;
		}

		#if ( S_WIND )
			o.vPositionWs.xyz += Wind_Offset( i.vPositionOs, o.vPositionWs.xyz, Wind_Mask( i.vColor ) );
		#endif

		// Recomputed unconditionally (not just under S_WIND): a landmark object needs its
		// clip position taken through the far-plane squash — no-op for everything else.
		o.vPositionPs = Landmark_Project( o.vPositionWs.xyz );

		return FinalizeVertex( o );
	}
}

PS
{
	// common/pixel.hlsl is safe without Material.CommonInputs.hlsl — material.hlsl guards its
	// texture references behind MATERIAL_COMMON_INPUTS_HLSL, so no PBR inputs leak into our UI.
	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"        // pulls DirectionalLightCB + DirectionalLightShadow
	#include "common/classes/AmbientLight.hlsl" // for the optional shadow-side lift

	StaticCombo( S_USE_TEXTURE, F_USE_TEXTURE, Sys( ALL ) );
	StaticCombo( S_USE_RAMP, F_USE_RAMP, Sys( ALL ) );
	StaticCombo( S_DISTANCE_FADE, F_DISTANCE_FADE, Sys( ALL ) );

	#include "shaders/heden_distance_fade.hlsl"

	// 0 = ignore renderer Tint, 1 = multiply, 2 = replace. See the albedo block in MainPs.
	float g_flObjectTintMode < Default( 0.0 ); Range( 0.0, 2.0 ); UiGroup( "Albedo,5/30" ); >;

	// Runestone rune surface (runtime-gated on RuneEnable — was the S_RUNE combo, zero
	// materials used it): code owns the color. RuneTint multiplies the lit result (rune
	// UVs sample near-white, so tint = true color); RuneGlow adds tint back on top —
	// values > 1 exceed LDR and feed bloom. Attribute defaults = the sleeping stone:
	// faint, slightly cool (the anti-redness lives here, not in the texture).
	float g_flRuneEnable < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Runestone,45/10" ); >;
	float3 g_vRuneTint < Attribute( "RuneTint" ); Default3( 0.80, 0.84, 0.90 ); >;
	float g_flRuneGlow < Attribute( "RuneGlow" ); Default( 0.35 ); >;

	#if ( S_USE_TEXTURE )
		CreateInputTexture2D( AlbedoTexture, Srgb, 8, "", "_color", "Albedo,5/10", Default3( 1.0, 1.0, 1.0 ) );
		Texture2D g_tAlbedo < Channel( RGB, Box( AlbedoTexture ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
		SamplerState g_sAlbedoSampler < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); >;
		float3 g_vTextureTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Albedo,5/20" ); >;
	#endif

	#include "shaders/heden_toon_core.hlsl"

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 worldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

		// BEFORE the depth early-out on purpose: the depth pass has to drop exactly the
		// pixels the forward pass drops, or dissolved geometry keeps occluding and keeps
		// casting a solid shadow. No-op unless S_DISTANCE_FADE.
		DistanceFade_Clip( worldPos, i.vPositionSs.xy );

		#if ( S_MODE_DEPTH )
			// Opaque — rasterized depth is all the shadow/prepass needs.
			return float4( 0.0, 0.0, 0.0, 1.0 );
		#endif

		// Albedo: either/or, never both (sRGB default-texture hybrids darken unpredictably).
		#if ( S_USE_TEXTURE )
			float3 albedo = g_tAlbedo.Sample( g_sAlbedoSampler, i.vTextureCoords.xy ).rgb * g_vTextureTint;
		#else
			float3 albedo = i.vVertexColor.rgb;
		#endif

		// Per-object Tint (ModelRenderer.Tint) mode — runtime, was the S_OBJECT_TINT combo:
		// 0 ignores it (a tinted renderer sharing this material is unaffected — what the
		// white of an eye wants), 1 multiplies, 2 replaces outright (exact recolour: the
		// hair/iris/brow materials).
		if ( g_flObjectTintMode > 1.5 )
			albedo = i.vObjectTint.rgb;
		else if ( g_flObjectTintMode > 0.5 )
			albedo *= i.vObjectTint.rgb;

		float3 normalWs = normalize( i.vNormalWs );

		// Style detail: triplanar texture modulates the albedo before shading, in whichever
		// frame the material's ObjectSpace knob picks (object = pattern rides movables).
		albedo = Detail_ApplyAlbedo( albedo,
			Detail_FramePos( worldPos, i.vDetailPosOs ),
			Detail_FrameNormal( normalWs, i.vDetailNormalOs ) );

		ToonLight sun = Toon_EvaluateSun( normalWs, worldPos, i.vPositionSs );

		float4 debugColor;
		if ( Toon_Debug( albedo, sun, normalWs, worldPos, debugColor ) )
			return debugColor;

		float3 color = Toon_Shade( albedo, sun, normalWs, worldPos, i.vPositionSs );

		if ( g_flRuneEnable > 0.5 )
		{
			// Hue-preserving glow: 0..1 blends the shaded rune toward its
			// pure self-lit color; beyond 1 adds overbright IN THAT HUE
			// (feeds bloom) instead of washing every channel to white.
			color *= g_vRuneTint;
			color = lerp( color, g_vRuneTint, saturate( g_flRuneGlow ) );
			color += g_vRuneTint * max( g_flRuneGlow - 1.0, 0.0 );
		}

		return float4( color, 1.0 );
	}
}
