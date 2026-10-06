// The potion stream (Code/Character/PotionPour.cs): a camera-facing strip from the bottle's lip into the mouth, shaded
// as if it were a round column of liquid in the game's toon style. U runs 0..1 across the strip, V is the distance
// along it in world units (so the pattern never stretches with the stream's length). The potion's colour arrives as
// vertex colour, the ends' fades as vertex alpha.
//
// Across: a dark rim band on both edges, the body colour, a light band and a hot glint off-centre (the column's
// highlight), all hard toon steps. Along: the light band and glint are broken into streaks by noise sliding toward the
// mouth, and the silhouette ripples slightly with it, so it reads as moving liquid, not a painted stripe.
//
// Structurally slash_ribbon.shader (Graphics.Draw vertex layout, texture slots, translucent-but-depth-writing states,
// the plain sampler: see the notes there, especially the bindless sampler crash). Materials must fill every texture input.

HEADER
{
	Description = "Potion stream - toon liquid column, unlit, translucent";
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

	float g_DepthFeather < Attribute( "g_DepthFeather" ); >;

	CreateInputTexture2D(TextureColor, Srgb, 8, "", "_color", "Material,10/10", Default4(1.0, 1.0, 1.0, 1.0));
	CreateInputTexture2D(TextureNormal, Linear, 8, "NormalizeNormals", "_normal", "Material,10/20", Default3(0.5, 0.5, 1.0));
	CreateInputTexture2D(TextureRoughness, Linear, 8, "", "_rough", "Material,10/30", Default(0.5));
	CreateInputTexture2D(TextureMetalness, Linear, 8, "", "_metal", "Material,10/40", Default(1.0));
	CreateInputTexture2D(TextureAmbientOcclusion, Linear, 8, "", "_ao", "Material,10/50", Default(1.0));

	Texture2D g_tColor < Channel(RGBA, Box(TextureColor), Srgb ); SrgbRead( true ); OutputFormat(BC7); >;
	Texture2D g_tNormal < Channel(RGB, Box(TextureNormal), Linear); OutputFormat(BC7); SrgbRead(false); > ;
	Texture2D g_tRma < Channel(R, Box(TextureRoughness), Linear); Channel(G, Box(TextureMetalness), Linear); Channel(B, Box(TextureAmbientOcclusion), Linear); OutputFormat(BC7); SrgbRead(false); > ;

	SamplerState g_sStreamSampler < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( WRAP ); >;

	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, true );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );

	// -- the column --------------------------------------------------------------------------
	float g_flRimWidth < Default( 0.2 ); Range( 0.0, 0.6 ); UiGroup( "Stream,10/10" ); >;
	float g_flRimDarken < Default( 0.5 ); Range( 0.0, 1.0 ); UiGroup( "Stream,10/11" ); >;
	// the light band: centre (-1 left edge .. 1 right edge), half-width, how far toward white
	float g_flLightCentre < Default( -0.35 ); Range( -1.0, 1.0 ); UiGroup( "Stream,10/20" ); >;
	float g_flLightWidth < Default( 0.22 ); Range( 0.0, 1.0 ); UiGroup( "Stream,10/21" ); >;
	float g_flLightAmount < Default( 0.35 ); Range( 0.0, 1.0 ); UiGroup( "Stream,10/22" ); >;
	// the glint inside the light band
	float g_flGlintWidth < Default( 0.07 ); Range( 0.0, 0.5 ); UiGroup( "Stream,10/30" ); >;
	float g_flGlintAmount < Default( 0.8 ); Range( 0.0, 1.0 ); UiGroup( "Stream,10/31" ); >;
	// -- the flow ----------------------------------------------------------------------------
	float g_flFlowSpeed < Default( 14.0 ); Range( 0.0, 60.0 ); UiGroup( "Stream,20/10" ); >;
	float g_flStreakLength < Default( 3.0 ); Range( 0.2, 20.0 ); UiGroup( "Stream,20/11" ); >;
	float g_flStreakBreak < Default( 0.45 ); Range( 0.0, 1.0 ); UiGroup( "Stream,20/12" ); >;
	float g_flRipple < Default( 0.07 ); Range( 0.0, 0.3 ); UiGroup( "Stream,20/20" ); >;
	float g_flRippleLength < Default( 1.6 ); Range( 0.2, 20.0 ); UiGroup( "Stream,20/21" ); >;
	float g_flBodyAlpha < Default( 0.94 ); Range( 0.2, 1.0 ); UiGroup( "Stream,20/30" ); >;
	// how soft the highlight's side edges are (across units, -1..1) and where a streak breaks along the stream (noise units)
	float g_flStreakSoft < Default( 0.06 ); Range( 0.0, 0.3 ); UiGroup( "Stream,20/40" ); >;
	float g_flBreakSoft < Default( 0.14 ); Range( 0.0, 0.5 ); UiGroup( "Stream,20/41" ); >;

	float Stream_Hash( float2 p )
	{
		float3 p3 = frac( float3( p.x, p.y, p.x ) * 0.1031 );
		p3 += dot( p3, p3.yzx + 33.33 );
		return frac( ( p3.x + p3.y ) * p3.z );
	}

	float2 Stream_Grad( float2 p )
	{
		float ang = Stream_Hash( p ) * 6.2831853;
		return float2( cos( ang ), sin( ang ) );
	}

	// gradient noise, roughly -0.5..0.5
	float Stream_Noise( float2 p )
	{
		float2 i = floor( p );
		float2 f = frac( p );
		float2 u = f * f * f * ( f * ( f * 6.0 - 15.0 ) + 10.0 );
		float a = dot( Stream_Grad( i ), f );
		float b = dot( Stream_Grad( i + float2( 1.0, 0.0 ) ), f - float2( 1.0, 0.0 ) );
		float c = dot( Stream_Grad( i + float2( 0.0, 1.0 ) ), f - float2( 0.0, 1.0 ) );
		float d = dot( Stream_Grad( i + float2( 1.0, 1.0 ) ), f - float2( 1.0, 1.0 ) );
		return lerp( lerp( a, b, u.x ), lerp( c, d, u.x ), u.y );
	}

	// a hard step with a one-pixel soft edge
	float Stream_Step( float edge, float x )
	{
		float w = max( fwidth( x ), 1e-4 );
		return smoothstep( edge - w, edge + w, x );
	}

	// a step softened over at least `soft`, and never less than a pixel and a half
	float Stream_SoftStep( float edge, float x, float soft )
	{
		float w = max( max( fwidth( x ) * 1.5, soft ), 1e-4 );
		return smoothstep( edge - w, edge + w, x );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float u = saturate( i.vTextureCoords.x );
		float along = i.vTextureCoords.y;                        // world units from the lip
		// the pattern slides toward the mouth; the clock wraps so the noise never runs on huge coordinates
		float flow = along - fmod( g_flTime * g_flFlowSpeed, 4096.0 );
		float4 texAlbedo = g_tColor.Sample( g_sStreamSampler, float2( u, 0.5 ) );

		float x = u * 2.0 - 1.0;                                 // -1 .. 1 across

		// the silhouette ripples as the liquid moves
		float ripple = Stream_Noise( float2( flow / g_flRippleLength, 3.7 ) ) * 2.0 * g_flRipple;
		float edge = 1.0 - g_flRipple + ripple;
		float coverage = 1.0 - Stream_Step( edge, abs( x ) );

		float3 bright = SrgbGammaToLinear( i.vVertexColor.rgb ) * texAlbedo.rgb;
		float3 color = bright;

		// dark rim on both sides: the column turning away
		float rim = Stream_Step( edge - g_flRimWidth, abs( x ) );
		color = lerp( color, bright * ( 1.0 - g_flRimDarken ), rim );

		// the highlight, broken into streaks that slide along
		// the streak mask depends only on the distance along (a mask that also varied across cut the band's sides into
		// jagged steps), and every edge of the band and the glint is a soft step, not a one-pixel one
		float streak = Stream_Noise( float2( flow / g_flStreakLength, 11.0 ) ) + 0.5;
		float lit = Stream_SoftStep( g_flStreakBreak, streak, g_flBreakSoft );
		float light = ( 1.0 - Stream_SoftStep( g_flLightWidth, abs( x - g_flLightCentre ), g_flStreakSoft ) ) * lit;
		color = lerp( color, lerp( bright, float3( 1.0, 1.0, 1.0 ), g_flLightAmount ), light );
		float glint = ( 1.0 - Stream_SoftStep( g_flGlintWidth, abs( x - g_flLightCentre ), g_flStreakSoft * 0.6 ) ) * Stream_SoftStep( g_flStreakBreak + 0.15, streak, g_flBreakSoft );
		color = lerp( color, float3( 1.0, 1.0, 1.0 ), glint * g_flGlintAmount );

		float alpha = coverage * g_flBodyAlpha * i.vVertexColor.a * texAlbedo.a;

		#if ( S_MODE_DEPTH )
			clip( alpha - 0.5 );
			return float4( 0.0, 0.0, 0.0, 1.0 );
		#endif

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
