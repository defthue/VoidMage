//=========================================================================================================================
// HEDEN — toon foliage shader (trees, bushes, leaf cards, decoration greenery)
//
// Same toon core as heden_toon.shader (shared include), plus:
//   - RGBA texture albedo with alpha cutout (discard) — cutout also applies in the depth pass so shadows respect it
//   - Two-sided rendering via the ENGINE'S F_RENDER_BACKFACES (already declared in vr_common_features.fxc
//     via common/features.hlsl — redeclaring it = "Found feature multiple times")
//   - Backface normal flip via SV_IsFrontFace (provided by common/pixelinput.hlsl when S_RENDER_BACKFACES is on)
//=========================================================================================================================

HEADER
{
	Description = "HEDEN toon foliage — alpha cutout, two-sided, shared toon banding";
}

FEATURES
{
	#include "common/features.hlsl"   // brings F_RENDER_BACKFACES — do not redeclare it
	Feature( F_USE_RAMP, 0..1, "Toon Lighting" );
	Feature( F_WIND, 0..1, "Wind" );
	Feature( F_WIND_MASK, 0..1, "Wind" );

	// Enable on materials drawn by InstancedPropBatch — dissolves them out before the
	// batch's hard MaxDrawDistance cull. Nearly free here: this shader already discards.
	Feature( F_DISTANCE_FADE, 0..1, "Distance Fade" );

	// Rim/highlight/style-detail are RUNTIME knobs (gated on their own values), not features —
	// see heden_toon.shader's permutation-budget note.
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

	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	// When S_RENDER_BACKFACES is on, pixelinput adds: bool face : SV_IsFrontFace

	// Object-space frame for the style-detail rig (heden_style_detail.hlsl) — see heden_toon.
	float3 vDetailPosOs : TEXCOORD14;
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
			// Respawn grow-in, same rule as heden_toon.
			i.vPositionOs.xyz *= g_flHedenTreeGrow;
		#endif

		PixelInput o = ProcessVertex( i );

		// Object-space frame for the style-detail rig — position after grow-in, see heden_toon.
		o.vDetailPosOs = i.vPositionOs.xyz;
		{
			float3 detailNormalOs; float4 detailTangentOs;
			VS_DecodeObjectSpaceNormalAndTangent( i, detailNormalOs, detailTangentOs );
			o.vDetailNormalOs = detailNormalOs;
		}

		#if ( S_WIND )
			o.vPositionWs.xyz += Wind_Offset( i.vPositionOs, o.vPositionWs.xyz, Wind_Mask( i.vColor ) );
		#endif

		// Unconditional for the same reason as heden_toon: the landmark far-plane squash
		// must see the final world position — no-op without HedenLandmarkFarZ.
		o.vPositionPs = Landmark_Project( o.vPositionWs.xyz );

		return FinalizeVertex( o );
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"
	#include "common/classes/AmbientLight.hlsl"

	StaticCombo( S_USE_RAMP, F_USE_RAMP, Sys( ALL ) );
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
	StaticCombo( S_DISTANCE_FADE, F_DISTANCE_FADE, Sys( ALL ) );

	#include "shaders/heden_distance_fade.hlsl"

	RenderState( CullMode, F_RENDER_BACKFACES ? NONE : DEFAULT );

	//---------------------------------------------------------------------------------------------------------------------
	// Albedo + opacity (RGBA)
	//---------------------------------------------------------------------------------------------------------------------
	CreateInputTexture2D( AlbedoTexture, Srgb, 8, "", "_color", "Foliage,5/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( OpacityTexture, Linear, 8, "", "_alpha", "Foliage,5/20", Default( 1.0 ) );
	Texture2D g_tAlbedo < Channel( RGB, Box( AlbedoTexture ), Srgb ); Channel( A, Box( OpacityTexture ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
	SamplerState g_sAlbedoSampler < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); >;

	float3 g_vFoliageTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Foliage,5/30" ); >;
	float g_flAlphaCutoff < Default( 0.5 ); Range( 0.0, 1.0 ); UiGroup( "Foliage,5/40" ); >;

	#include "shaders/heden_toon_core.hlsl"

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float4 texSample = g_tAlbedo.Sample( g_sAlbedoSampler, i.vTextureCoords.xy );

		// Cutout FIRST — must also run in the depth pass so cast shadows have holes.
		if ( texSample.a < g_flAlphaCutoff )
			discard;

		float3 worldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

		// Same reason as the cutout: the depth pass must drop what the forward pass drops,
		// or dissolved foliage still occludes and still casts a solid shadow.
		DistanceFade_Clip( worldPos, i.vPositionSs.xy );

		#if ( S_MODE_DEPTH )
			return float4( 0.0, 0.0, 0.0, 1.0 );
		#endif

		float3 albedo = texSample.rgb * g_vFoliageTint;

		float3 normalWs = normalize( i.vNormalWs );

		#if ( S_RENDER_BACKFACES )
			// Leaf card backs must light as if facing the viewer's side.
			if ( !i.face )
				normalWs = -normalWs;
		#endif

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

		return float4( color, 1.0 );
	}
}
