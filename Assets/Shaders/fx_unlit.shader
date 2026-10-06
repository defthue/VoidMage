// Unlit FX mesh: a flat colour times the object's Tint (ModelRenderer.Tint arrives in vVertexColor), no
// lighting, no textures, two-sided, no depth write, additive or alpha-blended by the vmat's F_ADDITIVE.
// For the Synty Particle FX meshes (crystals, shards, spikes, rings) that spells throw around.
// Skeleton from white_unlit.shader; a plain SamplerState would go here if a texture were ever added
// (never Bindless::GetSampler in a shader not fed by the Trail/LineRenderer: the slash_ribbon lesson).
HEADER
{
	Description = "VOID MAGE fx mesh: tinted unlit, additive or alpha";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ADDITIVE, 0..1, "Blend" );
}

MODES
{
	Forward();
	Depth();
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

	StaticCombo( S_ADDITIVE, F_ADDITIVE, Sys( ALL ) );

	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	#if S_ADDITIVE
		RenderState( SrcBlend, ONE );
		RenderState( DstBlend, ONE );
	#else
		RenderState( SrcBlend, SRC_ALPHA );
		RenderState( DstBlend, INV_SRC_ALPHA );
	#endif

	BoolAttribute( translucent, true );

	float3 g_vColor < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "FX,10/10" ); >;
	float g_flBrightness < Default( 1.0 ); Range( 0.0, 6.0 ); UiGroup( "FX,10/20" ); >;
	float g_flOpacity < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "FX,10/30" ); >;
	// a cheap silhouette softener: 0 = flat, 1 = edges fade to nothing
	float g_flRimFade < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "FX,10/40" ); >;
	float g_flRimPower < Default( 2.0 ); Range( 0.5, 6.0 ); UiGroup( "FX,10/41" ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 n = normalize( i.vNormalWs );
		float3 view = normalize( g_vCameraPositionWs - i.vPositionWithOffsetWs );
		float facing = saturate( abs( dot( n, view ) ) );
		float rim = lerp( 1.0, pow( facing, g_flRimPower ), g_flRimFade );
		float3 color = g_vColor * i.vVertexColor.rgb * g_flBrightness;
		float alpha = g_flOpacity * i.vVertexColor.a * rim;
		#if S_ADDITIVE
			return float4( color * alpha, alpha );
		#else
			return float4( color, alpha );
		#endif
	}
}
