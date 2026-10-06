//=========================================================================================================================
// HEDEN — toon GHOST shader: alpha-BLENDED toon surface for spectral parts (the SpiritDemon's wisps, the Mystic's arms)
//
// heden_foliage minus the cutout: the toon core lights the texture colour as usual, but the pixel is written with
// BlendEnable (src alpha / inv src alpha), no depth write and the `translucent` attribute, so it fades over what is
// behind it instead of punching a hole. Alpha is a MATERIAL KNOB (g_flOpacity) times an optional rim fade, not the
// texture's alpha: Synty's ghost UVs on the SpiritDemon sit on one texel whose alpha is 0.075 (their own shader
// was a soft blend over a gradient), so reading it would make him invisible. Two-sided via the engine's
// F_RENDER_BACKFACES. No Depth() mode: translucent geometry must not occlude or shadow.
//=========================================================================================================================

HEADER
{
	Description = "HEDEN toon ghost — alpha blended, two-sided, shared toon banding";
}

FEATURES
{
	#include "common/features.hlsl"   // brings F_RENDER_BACKFACES — do not redeclare it
	Feature( F_USE_RAMP, 0..1, "Toon Lighting" );
}

MODES
{
	Forward();
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

	float3 vDetailPosOs : TEXCOORD14;
	float3 vDetailNormalOs : TEXCOORD15;
};

VS
{
	#include "common/vertex.hlsl"

	#include "shaders/heden_landmark.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );

		o.vDetailPosOs = i.vPositionOs.xyz;
		{
			float3 detailNormalOs; float4 detailTangentOs;
			VS_DecodeObjectSpaceNormalAndTangent( i, detailNormalOs, detailTangentOs );
			o.vDetailNormalOs = detailNormalOs;
		}

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

	RenderState( CullMode, F_RENDER_BACKFACES ? NONE : DEFAULT );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );

	BoolAttribute( translucent, true );

	//---------------------------------------------------------------------------------------------------------------------
	// Albedo (the texture's alpha is NOT read, see the header)
	//---------------------------------------------------------------------------------------------------------------------
	CreateInputTexture2D( AlbedoTexture, Srgb, 8, "", "_color", "Ghost,5/10", Default3( 1.0, 1.0, 1.0 ) );
	Texture2D g_tAlbedo < Channel( RGB, Box( AlbedoTexture ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	SamplerState g_sAlbedoSampler < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); >;

	float3 g_vGhostTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Ghost,5/20" ); >;
	float g_flOpacity < Default( 0.35 ); Range( 0.0, 1.0 ); UiGroup( "Ghost,5/30" ); >;
	// silhouette softener: 0 = flat, 1 = edges fade to nothing (fx_unlit's rim rule)
	float g_flRimFade < Default( 0.5 ); Range( 0.0, 1.0 ); UiGroup( "Ghost,5/40" ); >;
	float g_flRimPower < Default( 2.0 ); Range( 0.5, 6.0 ); UiGroup( "Ghost,5/41" ); >;

	#include "shaders/heden_toon_core.hlsl"

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float4 texSample = g_tAlbedo.Sample( g_sAlbedoSampler, i.vTextureCoords.xy );

		float3 worldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

		float3 albedo = texSample.rgb * g_vGhostTint;

		float3 normalWs = normalize( i.vNormalWs );

		#if ( S_RENDER_BACKFACES )
			if ( !i.face )
				normalWs = -normalWs;
		#endif

		albedo = Detail_ApplyAlbedo( albedo,
			Detail_FramePos( worldPos, i.vDetailPosOs ),
			Detail_FrameNormal( normalWs, i.vDetailNormalOs ) );

		ToonLight sun = Toon_EvaluateSun( normalWs, worldPos, i.vPositionSs );

		float4 debugColor;
		if ( Toon_Debug( albedo, sun, normalWs, worldPos, debugColor ) )
			return debugColor;

		float3 color = Toon_Shade( albedo, sun, normalWs, worldPos, i.vPositionSs );

		float3 view = normalize( g_vCameraPositionWs - i.vPositionWithOffsetWs.xyz );
		float facing = saturate( abs( dot( normalWs, view ) ) );
		float rim = lerp( 1.0, pow( facing, g_flRimPower ), g_flRimFade );

		return float4( color, g_flOpacity * rim );
	}
}
