// The ray beam's laser ribbon — the golem-laser construction: a camera-facing line whose
// "roundness" is painted as a stepped gradient ACROSS the beam's width. t = |v*2-1| is 0
// down the center line and 1 at the edges; the profile is a white-hot over-bright core
// stripe (bloom feed), a red body, one hard darker shade step (the toonification), and a
// soft alpha falloff edge. No normals, no mesh — the LineRenderer supplies camera-facing
// geometry, this shader supplies the entire look.
//
// Structurally a fork of heden_trail_unlit.shader (the engine line vertex layout, combos,
// render states and texture slots, all verbatim — that shader is proven in this project);
// only the pixel logic differs. Length-wise fades ride the line's vertex color gradient
// from code, because line U is in texture-repeat units, not normalized over the line.
// Materials using it must fill every texture input — an empty slot binds the error texture.

HEADER
{
	Description = "HEDEN laser ribbon — stepped cross-width profile, unlit";
}

FEATURES
{
	#include "vr_common_features.fxc"
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

		// Bias position to reduce Shadow Acne
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

	DynamicCombo( D_OPAQUE, 0..1, Sys( ALL ) );
	DynamicCombo( D_WIREFRAME, 0..1, Sys( ALL ) );
	DynamicCombo( D_BLEND, 0..1, Sys( ALL ) );
	DynamicCombo( D_ENABLE_LIGHTING, 0..1, Sys( ALL ) );

	float g_DepthFeather < Attribute( "g_DepthFeather" ); >;
	float g_FogStrength < Attribute( "g_FogStrength" ); >;

	int SamplerIndex < Attribute("SamplerIndex"); >;

	CreateInputTexture2D(TextureColor, Srgb, 8, "", "_color", "Material,10/10", Default4(1.0, 1.0, 1.0, 1.0));
	CreateInputTexture2D(TextureNormal, Linear, 8, "NormalizeNormals", "_normal", "Material,10/20", Default3(0.5, 0.5, 1.0));
	CreateInputTexture2D(TextureRoughness, Linear, 8, "", "_rough", "Material,10/30", Default(0.5));
	CreateInputTexture2D(TextureMetalness, Linear, 8, "", "_metal", "Material,10/40", Default(1.0));
	CreateInputTexture2D(TextureAmbientOcclusion, Linear, 8, "", "_ao", "Material,10/50", Default(1.0));

	Texture2D g_tColor < Channel(RGBA, Box(TextureColor), Srgb ); SrgbRead( true ); OutputFormat(BC7); >;
	Texture2D g_tNormal < Channel(RGB, Box(TextureNormal), Linear); OutputFormat(BC7); SrgbRead(false); > ;
	Texture2D g_tRma < Channel(R, Box(TextureRoughness), Linear); Channel(G, Box(TextureMetalness), Linear); Channel(B, Box(TextureAmbientOcclusion), Linear); OutputFormat(BC7); SrgbRead(false); > ;

	RenderState( DepthWriteEnable, true );
	RenderState( CullMode, D_ENABLE_LIGHTING == 1 && D_OPAQUE == 0 ? DEFAULT : NONE );

	#if S_MODE_DEPTH == 0
		RenderState( DepthWriteEnable, false );
	#endif

	// Additive
	#if D_BLEND
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, SRC_ALPHA );
		RenderState( DstBlend, ONE );
		RenderState( DepthWriteEnable, false );
	#endif

	#if D_OPAQUE == 1
		RenderState( DepthWriteEnable, true );
		RenderState( BlendEnable, false );
		RenderState( AlphaToCoverageEnable, S_MODE_DEPTH == 0 );
	#elif S_MODE_DEPTH == 0
		RenderState( DepthWriteEnable, false );
	#endif

	#if D_WIREFRAME
		RenderState( FillMode, WIREFRAME );
	#endif

	// -- the laser profile ---------------------------------------------------------------

	float3 g_vColorHot < UiType( Color ); Default3( 1.0, 0.92, 0.9 ); UiGroup( "Beam,10/10" ); >;
	float3 g_vColorBody < UiType( Color ); Default3( 1.0, 0.16, 0.12 ); UiGroup( "Beam,10/11" ); >;
	float3 g_vColorShade < UiType( Color ); Default3( 0.45, 0.04, 0.04 ); UiGroup( "Beam,10/12" ); >;

	// A float, not a color picker — pickers clamp at 1 and the core must exceed 1 to feed
	// bloom, which is most of what makes this read as a laser.
	float g_flCoreBrightness < Default( 2.0 ); Range( 1.0, 6.0 ); UiGroup( "Beam,10/13" ); >;

	// Where along t = |v*2-1| (0 center .. 1 edge) each zone lives.
	float g_flCoreWidth < Default( 0.22 ); Range( 0.02, 0.8 ); UiGroup( "Beam,10/20" ); >;
	float g_flShadeStart < Default( 0.62 ); Range( 0.1, 1.0 ); UiGroup( "Beam,10/21" ); >;
	float g_flEdgeFadeStart < Default( 0.78 ); Range( 0.2, 1.0 ); UiGroup( "Beam,10/22" ); >;
	float g_flStepSoftness < Default( 0.02 ); Range( 0.002, 0.2 ); UiGroup( "Beam,10/23" ); >;

	// Brightness shimmer racing along the length — gradient noise so thresholded surges
	// stay rounded (heden_water's construction).
	float g_flEnergyTiling < Default( 0.02 ); Range( 0.0, 0.2 ); UiGroup( "Beam,10/30" ); >;
	float g_flEnergySpeed < Default( 6.0 ); Range( 0.0, 30.0 ); UiGroup( "Beam,10/31" ); >;
	float g_flEnergyStrength < Default( 0.25 ); Range( 0.0, 1.0 ); UiGroup( "Beam,10/32" ); >;

	float Beam_Hash( float2 p )
	{
		float3 p3 = frac( float3( p.x, p.y, p.x ) * 0.1031 );
		p3 += dot( p3, p3.yzx + 33.33 );
		return frac( ( p3.x + p3.y ) * p3.z );
	}

	float2 Beam_Grad( float2 p )
	{
		float ang = Beam_Hash( p ) * 6.2831853;
		return float2( cos( ang ), sin( ang ) );
	}

	float Beam_GradNoise( float2 p )
	{
		float2 i = floor( p );
		float2 f = frac( p );
		float2 u = f * f * f * ( f * ( f * 6.0 - 15.0 ) + 10.0 );

		float a = dot( Beam_Grad( i ), f );
		float b = dot( Beam_Grad( i + float2( 1.0, 0.0 ) ), f - float2( 1.0, 0.0 ) );
		float c = dot( Beam_Grad( i + float2( 0.0, 1.0 ) ), f - float2( 0.0, 1.0 ) );
		float d = dot( Beam_Grad( i + float2( 1.0, 1.0 ) ), f - float2( 1.0, 1.0 ) );

		return lerp( lerp( a, b, u.x ), lerp( c, d, u.x ), u.y );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		#if ( S_MODE_DEPTH )
			return float4( 0.0, 0.0, 0.0, 1.0 );
		#endif

		SamplerState samp = Bindless::GetSampler( SamplerIndex );

		float2 uv = i.vTextureCoords.xy;
		float4 texAlbedo = g_tColor.Sample( samp, uv );

		// 0 down the beam's center line, 1 at its edges — the whole profile hangs off it.
		// Line UVs, established in-game the hard way: U sweeps 0..1 ACROSS the width, V
		// runs ALONG the length in texture-repeat units (it saturates within the first
		// stretch — useless for anything normalized; length fades ride the vertex color
		// gradient, and the scrolling noise samples raw V happily in repeat units).
		float t = abs( uv.x * 2.0 - 1.0 );

		float s = g_flStepSoftness;

		float3 color = g_vColorBody;
		color = lerp( color, g_vColorShade, smoothstep( g_flShadeStart - s, g_flShadeStart + s, t ) );

		// Body shimmer applied before the core takes over, so the center stripe stays a
		// steady blazing line while the body breathes around it. V is the along-length
		// coordinate here (see above).
		float energy = Beam_GradNoise( float2( uv.y * g_flEnergyTiling * 64.0 - g_flTime * g_flEnergySpeed, 0.5 ) );
		color *= 1.0 + energy * g_flEnergyStrength;

		color = lerp( color, g_vColorHot * g_flCoreBrightness, 1.0 - smoothstep( g_flCoreWidth - s, g_flCoreWidth + s, t ) );

		float alpha = 1.0 - smoothstep( g_flEdgeFadeStart, 1.0, t );

		// The code-driven tint and length gradient (SrgbGammaToLinear per the trail
		// shader's convention).
		float4 vertexColor = float4( SrgbGammaToLinear( i.vVertexColor.rgb ), i.vVertexColor.a );
		color *= texAlbedo.rgb * vertexColor.rgb;
		alpha *= texAlbedo.a * vertexColor.a;

		// Soft landing where the ribbon crosses geometry, exactly the trail shader's.
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
