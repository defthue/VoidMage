// The sword slash ribbon: a strip swept by the blade (Code/Character/SwordSlash.cs builds it from
// the sword's real transform every frame and draws it with Graphics.Draw). The shader paints the
// crescent ACROSS the strip: U runs 0 at the inner edge (part-way up the blade) to 1 at the tip
// edge. Alpha fades toward the inner edge, a hot over-bright rim sits at the tip edge with one hard
// toon step, the outer few percent go soft. Fades ALONG the strip (age, head) ride the vertex
// colour from code, so this stays a pure cross-profile.
//
// Structurally a fork of heden_beam.shader (the engine line vertex layout, texture slots and
// depth feather, all verbatim). The dynamic D_BLEND/D_OPAQUE/D_ENABLE_LIGHTING combos are gone:
// only the TrailRenderer sets them, a Graphics.Draw call leaves them unset and would render the
// strip opaque, so the translucent states are hardcoded. Materials using it must fill every
// texture input: an empty slot binds the error texture.

HEADER
{
	Description = "Sword slash ribbon - crescent cross-profile, unlit, translucent";
}

FEATURES
{
	#include "vr_common_features.fxc"
	Feature( F_ADDITIVE, 0..1, "Blending" );
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
	float3 WorldPosition : POSITION < Semantic( None ); >;
	float3 Normal : NORMAL < Semantic( None ); >;
	float3 Tangent : Tangent < Semantic( None ); >;
	float4 Color : COLOR0 < Semantic( None ); >;
	float2 TextureCoords : TEXCOORD0 < Semantic( None ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	#if ( PROGRAM == VFX_PROGRAM_PS )
		bool face : SV_IsFrontFace;
	#endif
};

VS
{
	PixelInput MainVs( const VertexInput v )
	{
		PixelInput i;
		i.vPositionWs = v.WorldPosition - g_vHighPrecisionLightingOffsetWs.xyz;
		i.vPositionPs.xyzw = Position3WsToPs( v.WorldPosition );
		i.vNormalWs = v.Normal;
		i.vVertexColor = v.Color;
		i.vTextureCoords = float4( v.TextureCoords, v.TextureCoords );
		i.vTangentUWs = v.Tangent.xyz;
		i.vTangentVWs = normalize( cross( v.Normal.xyz, v.Tangent.xyz ) );

		#if ( S_MODE_DEPTH == 1 )
			i.vPositionPs.z = Position3WsToPs( v.WorldPosition + g_vCameraDirWs ).z;
		#endif

		return i;
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "common/blendmode.hlsl"

	// Additive variant for a glowing (fire, void) ribbon; the default is a translucent white streak.
	StaticCombo( S_ADDITIVE, F_ADDITIVE, Sys( ALL ) );

	float g_DepthFeather < Attribute( "g_DepthFeather" ); >;

	CreateInputTexture2D(TextureColor, Srgb, 8, "", "_color", "Material,10/10", Default4(1.0, 1.0, 1.0, 1.0));
	CreateInputTexture2D(TextureNormal, Linear, 8, "NormalizeNormals", "_normal", "Material,10/20", Default3(0.5, 0.5, 1.0));
	CreateInputTexture2D(TextureRoughness, Linear, 8, "", "_rough", "Material,10/30", Default(0.5));
	CreateInputTexture2D(TextureMetalness, Linear, 8, "", "_metal", "Material,10/40", Default(1.0));
	CreateInputTexture2D(TextureAmbientOcclusion, Linear, 8, "", "_ao", "Material,10/50", Default(1.0));

	Texture2D g_tColor < Channel(RGBA, Box(TextureColor), Srgb ); SrgbRead( true ); OutputFormat(BC7); >;
	Texture2D g_tNormal < Channel(RGB, Box(TextureNormal), Linear); OutputFormat(BC7); SrgbRead(false); > ;
	Texture2D g_tRma < Channel(R, Box(TextureRoughness), Linear); Channel(G, Box(TextureMetalness), Linear); Channel(B, Box(TextureAmbientOcclusion), Linear); OutputFormat(BC7); SrgbRead(false); > ;

	// A plainly declared sampler, NOT Bindless::GetSampler. The trail/beam shaders this was adapted
	// from are drawn by the engine's TrailRenderer, which sets the "SamplerIndex" attribute the
	// bindless path reads; this shader is drawn by SwordSlash's own Graphics.Draw with a hand-built
	// RenderAttributes that sets only g_DepthFeather, so the index stayed uninitialised and the
	// fetch landed on a garbage descriptor — an intermittent GPU page fault that killed the process
	// outright, with no managed exception. The stroke samples one texture with one filter, so the
	// bindless indirection bought nothing here. UVs run 0..1 across and along the stroke: clamp.
	SamplerState g_sSlashSampler < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;

	// Translucent, both faces (the strip twists). It DOES write depth: the retro filter's depth fog runs
	// after this pass and reads the depth buffer, so a stroke that left the wall's depth behind it was
	// fogged as if it were the wall (the arms write depth and stay clear). The stroke is near-opaque and
	// its two lines never overlap, so the blend/depth-write combination has nothing to sort wrongly.
	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, true );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	#if S_ADDITIVE
		RenderState( DstBlend, ONE );
	#else
		RenderState( DstBlend, INV_SRC_ALPHA );
	#endif

	// -- the stroke profile -------------------------------------------------------------
	// A flat, opaque stroke with crisp edges and a slightly brighter core line down its middle: the
	// reference slashes have no gradient across them. Fades along the arc come from code. t = |u*2-1|
	// is 0 on the stroke's centre line and 1 at its edges (the stroke is centred on the arc).

	float3 g_vColorHot < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Slash,10/10" ); >;
	float3 g_vColorBody < UiType( Color ); Default3( 0.9, 0.94, 1.0 ); UiGroup( "Slash,10/11" ); >;
	// Floats, not colour pickers: the core exceeds 1 to feed bloom, the body sits under the threshold.
	float g_flCoreBrightness < Default( 1.5 ); Range( 0.5, 6.0 ); UiGroup( "Slash,10/12" ); >;
	float g_flBodyBrightness < Default( 1.0 ); Range( 0.2, 3.0 ); UiGroup( "Slash,10/13" ); >;

	// Across t: how wide the core line is, where the edge starts softening, and the core's step width.
	float g_flCoreWidth < Default( 0.35 ); Range( 0.0, 1.0 ); UiGroup( "Slash,10/20" ); >;
	float g_flEdgeSoft < Default( 0.75 ); Range( 0.3, 1.0 ); UiGroup( "Slash,10/22" ); >;
	float g_flStepSoftness < Default( 0.15 ); Range( 0.002, 0.5 ); UiGroup( "Slash,10/23" ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float4 texAlbedo = g_tColor.Sample( g_sSlashSampler, uv );

		float t = abs( saturate( uv.x ) * 2.0 - 1.0 );
		float s = g_flStepSoftness;

		float3 color = lerp( g_vColorHot * g_flCoreBrightness, g_vColorBody * g_flBodyBrightness, smoothstep( g_flCoreWidth - s, g_flCoreWidth + s, t ) );
		float alpha = 1.0 - smoothstep( g_flEdgeSoft, 1.0, t );

		// The code-driven tint and age fade (SrgbGammaToLinear per the trail shader's convention).
		float4 vertexColor = float4( SrgbGammaToLinear( i.vVertexColor.rgb ), i.vVertexColor.a );
		color *= texAlbedo.rgb * vertexColor.rgb;
		alpha *= texAlbedo.a * vertexColor.a;

		// The depth prepass writes only where the stroke is actually visible (the object is opaque so its
		// depth reaches the fog; the erased and transparent parts must not leave depth behind).
		#if ( S_MODE_DEPTH )
			clip( alpha - 0.5 );
			return float4( 0.0, 0.0, 0.0, 1.0 );
		#endif

		// Soft landing where the strip crosses geometry, exactly the trail shader's.
		if ( g_DepthFeather > 0 )
		{
			float3 worldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
			float3 scenePos = Depth::GetWorldPosition( i.vPositionSs.xy );
			alpha *= clamp( distance( scenePos, worldPos ) / g_DepthFeather, 0.0, 1.0 );
		}

		clip( alpha - 0.001 );

		return float4( color, alpha );
	}
}
