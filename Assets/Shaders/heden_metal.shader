// voidmage: metal material for the blade and brass (see tools/README.md, "Weapon materials")
//=========================================================================================================================
// HEDEN — toon metal shader (blades, brass, armour)
//
// heden_toon's pipeline (texture/vertex albedo, ramp or bands, darkness zones, banded dynamic
// lights, fog — via heden_toon_core.hlsl) with the things that make a flat colour read as METAL,
// all as runtime knobs on the one compile:
//
//   Reflect*   a fake environment: the reflected view vector's world "up" is quantised into three
//              zones — sky (bright), horizon (dark), ground (mid) — that multiply the albedo. This
//              is the classic painted chrome: a horizon that sweeps across the blade as it turns.
//              Optional noise smudges the horizon so it looks painted, not ruled.
//   Brush*     brushed-metal streaks (heden_ice's crack generator) in OBJECT space, so they ride the
//              weapon; faint by default.
//   Edge*      view-only fresnel edge light added after the shade — metals brighten at grazing
//              angles, and unlike the core's rim it does not need the sun (torch-lit dungeons).
//   Glint*     heden_ice's light-driven catches: one hard Blinn spot per light (sun + cluster
//              lights), riding each light's colour, falloff and shadow.
//
// Reflection and brush are albedo work (before Toon_Shade: torch bands and darkness apply); edge
// light and glints are light (after it, fogged by difference). A broad sun sheen is the core's own
// Highlight Band knobs. Retro-filter rule: hard bands, few tones, nothing sub-pixel.
//=========================================================================================================================

HEADER
{
	Description = "HEDEN toon metal — fake reflection horizon, brushed streaks, edge light, light-driven glints";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_USE_TEXTURE, 0..1, "Albedo" );
	Feature( F_USE_RAMP, 0..1, "Toon Lighting" );
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

	// The per-object Tint (SceneObject.ColorTint) — see heden_toon.
	float4 vObjectTint : TEXCOORD14;

	// Object-space frame: the style-detail rig and the brushed streaks.
	float3 vDetailPosOs : TEXCOORD13;
	float3 vDetailNormalOs : TEXCOORD15;
};

VS
{
	#include "common/vertex.hlsl"
	#include "shaders/heden_landmark.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		o.vObjectTint = o.vVertexColor;
		o.vVertexColor = i.vColor;

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

	StaticCombo( S_USE_TEXTURE, F_USE_TEXTURE, Sys( ALL ) );
	StaticCombo( S_USE_RAMP, F_USE_RAMP, Sys( ALL ) );

	// 0 = ignore renderer Tint, 1 = multiply, 2 = replace (see heden_toon).
	float g_flObjectTintMode < Default( 0.0 ); Range( 0.0, 2.0 ); UiGroup( "Albedo,5/30" ); >;

	#if ( S_USE_TEXTURE )
		CreateInputTexture2D( AlbedoTexture, Srgb, 8, "", "_color", "Albedo,5/10", Default3( 1.0, 1.0, 1.0 ) );
		Texture2D g_tAlbedo < Channel( RGB, Box( AlbedoTexture ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
		SamplerState g_sAlbedoSampler < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); >;
		float3 g_vTextureTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Albedo,5/20" ); >;
	#endif

	#include "shaders/heden_toon_core.hlsl"

	//---------------------------------------------------------------------------------------------------------------------
	// Metal — noise source (the same tiling noise heden_ice uses)
	//---------------------------------------------------------------------------------------------------------------------
	CreateInputTexture2D( MetalNoise, Linear, 8, "", "_noise", "Metal,40/5", Default4( 0.5, 0.5, 0.5, 0.5 ) );
	Texture2D g_tMetalNoise < Channel( RGBA, Box( MetalNoise ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( false ); >;
	SamplerState g_sMetalNoise < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); >;

	//---------------------------------------------------------------------------------------------------------------------
	// Metal — fake reflection horizon
	//---------------------------------------------------------------------------------------------------------------------
	// 0 = flat toon colour. 1 = the three zone tints fully multiply the albedo.
	float g_flReflectStrength < Default( 0.5 ); Range( 0.0, 1.0 ); UiGroup( "Reflection,41/10" ); >;

	// What the metal "sees" above, across and below the horizon. Multipliers on the albedo:
	// above 1 is allowed (overbright sky catch).
	float3 g_vReflectSkyTint < UiType( Color ); Default3( 1.35, 1.35, 1.40 ); UiGroup( "Reflection,41/20" ); >;
	float3 g_vReflectHorizonTint < UiType( Color ); Default3( 0.45, 0.45, 0.50 ); UiGroup( "Reflection,41/30" ); >;
	float3 g_vReflectGroundTint < UiType( Color ); Default3( 0.80, 0.78, 0.75 ); UiGroup( "Reflection,41/40" ); >;

	// Where the horizon sits on the reflected up (-1 down .. 1 up), the dark band's half width,
	// and the edge softness of the zone cuts.
	float g_flReflectHorizon < Default( 0.0 ); Range( -1.0, 1.0 ); UiGroup( "Reflection,41/50" ); >;
	float g_flReflectWidth < Default( 0.25 ); Range( 0.02, 1.0 ); UiGroup( "Reflection,41/60" ); >;
	float g_flReflectSoftness < Default( 0.05 ); Range( 0.005, 0.5 ); UiGroup( "Reflection,41/70" ); >;

	// Wobbles the horizon with noise read along the reflected direction — painted smudges
	// instead of a ruled line. 0 = ruled.
	float g_flReflectNoise < Default( 0.25 ); Range( 0.0, 1.0 ); UiGroup( "Reflection,41/80" ); >;
	float g_flReflectNoiseScale < Default( 0.5 ); Range( 0.05, 4.0 ); UiGroup( "Reflection,41/90" ); >;

	//---------------------------------------------------------------------------------------------------------------------
	// Metal — brushed streaks (object space)
	//---------------------------------------------------------------------------------------------------------------------
	float g_flBrushStrength < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Brush,42/10" ); >;
	// Object units between streaks.
	float g_flBrushScale < Default( 0.6 ); Range( 0.05, 16.0 ); UiGroup( "Brush,42/20" ); >;
	// Direction of the streaks on the surface, degrees.
	float g_flBrushAngle < Default( 90.0 ); Range( 0.0, 180.0 ); UiGroup( "Brush,42/30" ); >;
	float g_flBrushStretch < Default( 6.0 ); Range( 1.0, 24.0 ); UiGroup( "Brush,42/40" ); >;

	//---------------------------------------------------------------------------------------------------------------------
	// Metal — edge light (view-only fresnel, after the shade)
	//---------------------------------------------------------------------------------------------------------------------
	float3 g_vEdgeColor < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Edge Light,43/10" ); >;
	float g_flEdgeStrength < Default( 0.0 ); Range( 0.0, 2.0 ); UiGroup( "Edge Light,43/20" ); >;
	float g_flEdgeThreshold < Default( 0.65 ); Range( 0.0, 1.0 ); UiGroup( "Edge Light,43/30" ); >;
	float g_flEdgeSoftness < Default( 0.05 ); Range( 0.005, 0.5 ); UiGroup( "Edge Light,43/40" ); >;

	//---------------------------------------------------------------------------------------------------------------------
	// Metal — glints (heden_ice's light-driven catches)
	//---------------------------------------------------------------------------------------------------------------------
	float3 g_vGlintTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Glint,44/10" ); >;
	float g_flGlintIntensity < Default( 1.0 ); Range( 0.0, 3.0 ); UiGroup( "Glint,44/20" ); >;
	float g_flGlintPower < Default( 32.0 ); Range( 2.0, 128.0 ); UiGroup( "Glint,44/30" ); >;
	float g_flGlintThreshold < Default( 0.5 ); Range( 0.0, 1.0 ); UiGroup( "Glint,44/40" ); >;
	float g_flGlintSoftness < Default( 0.08 ); Range( 0.005, 0.5 ); UiGroup( "Glint,44/50" ); >;

	float Metal_GlintBand( float3 normalWs, float3 lightDir, float3 viewDir )
	{
		float3 h = normalize( lightDir + viewDir );
		float spec = pow( saturate( dot( normalWs, h ) ), g_flGlintPower );

		float s = max( g_flGlintSoftness, 0.005 );
		return smoothstep( g_flGlintThreshold - s, g_flGlintThreshold + s, spec );
	}

	float3 Metal_Glints( float3 normalWs, float3 worldPos, float4 screenPos, float3 viewDir, ToonLight sun )
	{
		float3 total = Metal_GlintBand( normalWs, sun.SunDir, viewDir ) * sun.Lit * sun.SunTint;

		uint lightCount = Light::Count( screenPos );

		[loop]
		for ( uint li = 0; li < lightCount; li++ )
		{
			Light light = Light::From( worldPos, screenPos, li );

			if ( light.LightData.Type == LightType::LightTypeDirectional )
				continue;
			if ( !light.LightData.IsDiffuseEnabled() )
				continue;
			if ( dot( normalWs, light.Direction ) <= 0.0 )
				continue;

			float atten = light.Attenuation * light.Visibility;
			if ( atten <= 0.0 )
				continue;

			total += light.Color * ( Metal_GlintBand( normalWs, light.Direction, viewDir ) * atten );
		}

		return total * g_vGlintTint * g_flGlintIntensity;
	}

	float2 Metal_ProjectUv( float3 p, float3 normal, float scale )
	{
		float3 an = abs( normal );
		float2 uv = ( an.z >= max( an.x, an.y ) ) ? p.xy : ( ( an.x >= an.y ) ? p.yz : p.xz );
		return uv / max( scale, 0.001 );
	}

	// Brushed streaks: heden_ice's crack generator, rotated and stretched, in object space.
	float Metal_Brush( float3 posOs, float3 normalOs )
	{
		float2 uv = Metal_ProjectUv( posOs, normalOs, g_flBrushScale );

		float sa, ca;
		sincos( radians( g_flBrushAngle ), sa, ca );
		uv = float2( uv.x * ca - uv.y * sa, uv.x * sa + uv.y * ca );
		uv.y /= max( g_flBrushStretch, 1.0 );

		float wobble = g_tMetalNoise.Sample( g_sMetalNoise, uv * float2( 0.31, 0.11 ) ).g - 0.5;
		float line = 1.0 - abs( frac( uv.x + wobble * 0.8 ) - 0.5 ) * 2.0;
		float gate = g_tMetalNoise.Sample( g_sMetalNoise, uv * float2( 0.13, 0.47 ) + 7.31 ).a;
		return line * ( 0.55 + 0.45 * gate );
	}

	// The fake environment: which of three zones the reflected view ray points into.
	float3 Metal_Reflection( float3 normalWs, float3 viewDirWs )
	{
		float3 r = reflect( -viewDirWs, normalWs );
		float up = r.z;

		if ( g_flReflectNoise > 0.0 )
		{
			// Noise along the reflected direction: the smudge is pinned to the environment,
			// so it slides over the surface as the metal turns — like a real reflection.
			float2 nuv = r.xy * ( 0.5 / max( g_flReflectNoiseScale, 0.01 ) ) + r.z * 0.37;
			up += ( g_tMetalNoise.Sample( g_sMetalNoise, nuv ).g - 0.5 ) * g_flReflectNoise;
		}

		float s = max( g_flReflectSoftness, 0.005 );
		float hi = g_flReflectHorizon + g_flReflectWidth;
		float lo = g_flReflectHorizon - g_flReflectWidth;
		float sky = smoothstep( hi - s, hi + s, up );
		float ground = 1.0 - smoothstep( lo - s, lo + s, up );

		float3 zone = g_vReflectHorizonTint;
		zone = lerp( zone, g_vReflectSkyTint, sky );
		zone = lerp( zone, g_vReflectGroundTint, ground );
		return lerp( float3( 1.0, 1.0, 1.0 ), zone, g_flReflectStrength );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 worldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

		#if ( S_MODE_DEPTH )
			return float4( 0.0, 0.0, 0.0, 1.0 );
		#endif

		#if ( S_USE_TEXTURE )
			float3 albedo = g_tAlbedo.Sample( g_sAlbedoSampler, i.vTextureCoords.xy ).rgb * g_vTextureTint;
		#else
			float3 albedo = i.vVertexColor.rgb;
		#endif

		if ( g_flObjectTintMode > 1.5 )
			albedo = i.vObjectTint.rgb;
		else if ( g_flObjectTintMode > 0.5 )
			albedo *= i.vObjectTint.rgb;

		float3 normalWs = normalize( i.vNormalWs );
		float3 viewDirWs = normalize( g_vCameraPositionWs.xyz - worldPos );

		// Albedo work: the reflection zones and the brushed grain, before lighting.
		if ( g_flReflectStrength > 0.0 )
			albedo *= Metal_Reflection( normalWs, viewDirWs );

		if ( g_flBrushStrength > 0.0 )
		{
			float b = Metal_Brush( i.vDetailPosOs, normalize( i.vDetailNormalOs ) );
			albedo *= 1.0 + ( b - 0.5 ) * g_flBrushStrength;
		}

		albedo = Detail_ApplyAlbedo( albedo,
			Detail_FramePos( worldPos, i.vDetailPosOs ),
			Detail_FrameNormal( normalWs, i.vDetailNormalOs ) );

		ToonLight sun = Toon_EvaluateSun( normalWs, worldPos, i.vPositionSs );

		float4 debugColor;
		if ( Toon_Debug( albedo, sun, normalWs, worldPos, debugColor ) )
			return debugColor;

		float3 color = Toon_Shade( albedo, sun, normalWs, worldPos, i.vPositionSs );

		// Light work after the shade, fogged by difference (see heden_ice).
		float3 add = float3( 0.0, 0.0, 0.0 );

		if ( g_flEdgeStrength > 0.0 )
		{
			float fresnel = 1.0 - saturate( dot( normalWs, viewDirWs ) );
			float s = max( g_flEdgeSoftness, 0.005 );
			float band = smoothstep( g_flEdgeThreshold - s, g_flEdgeThreshold + s, fresnel );
			add += g_vEdgeColor * ( band * g_flEdgeStrength );
		}

		if ( g_flGlintIntensity > 0.0 )
			add += Metal_Glints( normalWs, worldPos, i.vPositionSs, viewDirWs, sun );

		if ( any( add > 0.0 ) )
		{
			color += Landmark_Fog( worldPos, i.vPositionSs.xy, add )
				- Landmark_Fog( worldPos, i.vPositionSs.xy, float3( 0.0, 0.0, 0.0 ) );
		}

		return float4( color, 1.0 );
	}
}
