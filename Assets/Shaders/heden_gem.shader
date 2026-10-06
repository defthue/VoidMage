// voidmage: gem material for the sword and staff gems (see tools/README.md, "Weapon materials")
//=========================================================================================================================
// HEDEN — toon gem shader (heden_ice grown into cut stones)
//
// heden_ice.shader's whole recipe (fake interior depth, gloss streaks, light-driven glints over
// the toon core) plus a "Gem" knob set. Every knob defaults to the ice behaviour, so a material
// that only sets the ice keys renders exactly like heden_ice — heden's ore vmats can point here
// unchanged, and the gem look is purely what the new keys add:
//
//   PatternObjectSpace  the interior/gloss patterns live in the MESH's frame, with the view ray
//                       expressed in that frame (VS interpolant). A hand-held gem then keeps its
//                       inside still as you walk and only shifts with the VIEW — the parallax that
//                       reads as depth — instead of crawling through a world-glued projection.
//   Refraction          the interior ray is bent by a fake IOR before it dives, so the inside
//                       shifts more at grazing angles the way a cut stone's does.
//   Edge*               fresnel-weighted darkening toward a deep saturated tint: the painted gem's
//                       dark rim (ice does the opposite with its floated gloss).
//   Core*               additive self-glow concentrated at the centre facing the viewer (a lit
//                       window), added AFTER the shade so darkness cannot eat it; above 1 goes
//                       overbright in hue for bloom. The one deliberate break with the ice's
//                       no-self-glow rule: a magic gem must read in a black corridor.
//   Twinkle             the glint threshold wanders with slow noise, so facet catches under a
//                       flickering torch shimmer instead of sitting still.
//
// Retro-filter rule (voidmage): every effect is a hard band with few tones; nothing sub-pixel.
//=========================================================================================================================

HEADER
{
	Description = "HEDEN toon gem — ice interior/gloss/glints + object-space patterns, refraction, dark rim, core glow";
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

	// Direction from the surface to the camera, in OBJECT space (see PatternObjectSpace).
	float3 vViewDirOs : TEXCOORD13;

	// Object-space frame for the style-detail rig (heden_style_detail.hlsl) — see heden_toon.
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
		o.vVertexColor = i.vColor;

		// Object-space frame for the style-detail rig — see heden_toon.
		o.vDetailPosOs = i.vPositionOs.xyz;
		{
			float3 detailNormalOs; float4 detailTangentOs;
			VS_DecodeObjectSpaceNormalAndTangent( i, detailNormalOs, detailTangentOs );
			o.vDetailNormalOs = detailNormalOs;
		}

		// The view ray in the mesh's own frame: a world direction through the transposed
		// object-to-world rotation (exact for rigid + uniform scale, which is every prop).
		{
			float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );
			float3 toCameraWs = g_vCameraPositionWs.xyz - o.vPositionWs.xyz;
			o.vViewDirOs = normalize( mul( toCameraWs, (float3x3)matObjectToWorld ) );
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

	#if ( S_USE_TEXTURE )
		CreateInputTexture2D( AlbedoTexture, Srgb, 8, "", "_color", "Albedo,5/10", Default3( 1.0, 1.0, 1.0 ) );
		Texture2D g_tAlbedo < Channel( RGB, Box( AlbedoTexture ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
		SamplerState g_sAlbedoSampler < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); >;
		float3 g_vTextureTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Albedo,5/20" ); >;
	#endif

	#include "shaders/heden_toon_core.hlsl"

	//---------------------------------------------------------------------------------------------------------------------
	// Ice — noise source (same key as heden_ice, so ore vmats carry over)
	//---------------------------------------------------------------------------------------------------------------------
	CreateInputTexture2D( IceNoise, Linear, 8, "", "_noise", "Ice,40/5", Default4( 0.5, 0.5, 0.5, 0.5 ) );
	Texture2D g_tIceNoise < Channel( RGBA, Box( IceNoise ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( false ); >;
	SamplerState g_sIceNoise < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); >;

	//---------------------------------------------------------------------------------------------------------------------
	// Ice — interior (verbatim knobs from heden_ice)
	//---------------------------------------------------------------------------------------------------------------------
	float g_flIceDepth < Default( 8.0 ); Range( 0.0, 40.0 ); UiGroup( "Ice Interior,41/10" ); >;
	float g_flInteriorScale < Default( 48.0 ); Range( 1.0, 256.0 ); UiGroup( "Ice Interior,41/20" ); >;
	float3 g_vInteriorTint < UiType( Color ); Default3( 0.55, 0.72, 1.0 ); UiGroup( "Ice Interior,41/30" ); >;
	float g_flInteriorStrength < Default( 0.6 ); Range( 0.0, 1.0 ); UiGroup( "Ice Interior,41/40" ); >;
	float g_flInteriorThreshold < Default( 0.55 ); Range( 0.0, 1.0 ); UiGroup( "Ice Interior,41/50" ); >;
	float g_flInteriorSoftness < Default( 0.08 ); Range( 0.005, 0.5 ); UiGroup( "Ice Interior,41/60" ); >;
	float g_flInteriorRidge < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Ice Interior,41/70" ); >;
	float g_flInteriorReplace < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Ice Interior,41/80" ); >;
	float g_flInteriorDrift < Default( 0.0 ); Range( 0.0, 8.0 ); UiGroup( "Ice Interior,41/90" ); >;
	float g_flInteriorStretch < Default( 1.0 ); Range( 1.0, 12.0 ); UiGroup( "Ice Interior,41/95" ); >;
	float g_flInteriorStreaks < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Ice Interior,41/97" ); >;
	float g_flInteriorAngle < Default( 0.0 ); Range( 0.0, 180.0 ); UiGroup( "Ice Interior,41/98" ); >;
	float g_flInteriorFollowAxis < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Ice Interior,41/99" ); >;
	float g_flInteriorBands < Default( 4.0 ); Range( 1.0, 16.0 ); UiGroup( "Ice Interior,41/100" ); >;

	//---------------------------------------------------------------------------------------------------------------------
	// Ice — gloss (verbatim knobs from heden_ice)
	//---------------------------------------------------------------------------------------------------------------------
	float3 g_vGlossTint < UiType( Color ); Default3( 0.95, 0.99, 1.0 ); UiGroup( "Ice Gloss,42/10" ); >;
	float g_flGlossStrength < Default( 0.85 ); Range( 0.0, 1.0 ); UiGroup( "Ice Gloss,42/20" ); >;
	float g_flGlossScale < Default( 64.0 ); Range( 1.0, 256.0 ); UiGroup( "Ice Gloss,42/30" ); >;
	float g_flGlossThreshold < Default( 0.62 ); Range( 0.0, 1.0 ); UiGroup( "Ice Gloss,42/40" ); >;
	float g_flGlossSoftness < Default( 0.06 ); Range( 0.005, 0.5 ); UiGroup( "Ice Gloss,42/50" ); >;
	float g_flGlossFresnel < Default( 1.5 ); Range( 0.0, 6.0 ); UiGroup( "Ice Gloss,42/60" ); >;
	float g_flGlossFloat < Default( 3.0 ); Range( 0.0, 15.0 ); UiGroup( "Ice Gloss,42/70" ); >;

	//---------------------------------------------------------------------------------------------------------------------
	// Ice — glints (verbatim knobs from heden_ice)
	//---------------------------------------------------------------------------------------------------------------------
	float3 g_vGlintTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Ice Glint,43/10" ); >;
	float g_flGlintIntensity < Default( 0.8 ); Range( 0.0, 3.0 ); UiGroup( "Ice Glint,43/20" ); >;
	float g_flGlintPower < Default( 24.0 ); Range( 2.0, 128.0 ); UiGroup( "Ice Glint,43/30" ); >;
	float g_flGlintThreshold < Default( 0.5 ); Range( 0.0, 1.0 ); UiGroup( "Ice Glint,43/40" ); >;
	float g_flGlintSoftness < Default( 0.08 ); Range( 0.005, 0.5 ); UiGroup( "Ice Glint,43/50" ); >;

	//---------------------------------------------------------------------------------------------------------------------
	// Gem — the additions. All zero = heden_ice.
	//---------------------------------------------------------------------------------------------------------------------
	// 0 = patterns projected in world space (ice, static rocks). 1 = in the mesh's frame, view
	// ray included: for anything that moves (weapons, pickups), otherwise the inside swims.
	float g_flPatternObjectSpace < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Gem,44/10" ); >;

	// Bends the interior ray like glass/stone would (0 = straight view ray, 1 = ruby-hard bend):
	// the inside slides further at grazing angles, which is what a cut facet does.
	float g_flRefraction < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Gem,44/20" ); >;

	// Dark saturated rim at silhouettes (the painted gem's edge). Tint multiplies the albedo,
	// Strength is how far toward it, Power how tight to the edge (higher = thinner rim).
	float3 g_vEdgeTint < UiType( Color ); Default3( 0.35, 0.02, 0.06 ); UiGroup( "Gem,44/30" ); >;
	float g_flEdgeStrength < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Gem,44/40" ); >;
	float g_flEdgePower < Default( 2.0 ); Range( 0.5, 8.0 ); UiGroup( "Gem,44/50" ); >;

	// Self-lit core: the window facing the viewer glows in this colour. Glow is the strength
	// (>1 = overbright, feeds bloom), Focus how concentrated toward the centre.
	float3 g_vCoreColor < UiType( Color ); Default3( 1.0, 0.25, 0.15 ); UiGroup( "Gem,44/60" ); >;
	float g_flCoreGlow < Default( 0.0 ); Range( 0.0, 3.0 ); UiGroup( "Gem,44/70" ); >;
	float g_flCoreFocus < Default( 2.0 ); Range( 0.5, 8.0 ); UiGroup( "Gem,44/80" ); >;

	// How much the glint threshold wanders over time (facets flicker on and off).
	float g_flTwinkle < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Gem,44/90" ); >;
	float g_flTwinkleSpeed < Default( 0.3 ); Range( 0.0, 3.0 ); UiGroup( "Gem,44/95" ); >;

	float Ice_AxisBands( float3 axisCoord, float2 uv, float phase )
	{
		float wobble = g_tIceNoise.Sample( g_sIceNoise, uv * float2( 0.31, 0.11 ) ).g - 0.5;
		float bands = max( round( g_flInteriorBands ), 1.0 );
		float line = 1.0 - abs( frac( axisCoord.y * bands + phase + wobble * 0.4 ) - 0.5 ) * 2.0;
		float gate = g_tIceNoise.Sample( g_sIceNoise, uv * float2( 0.13, 0.47 ) + 7.31 ).a;
		return line * ( 0.45 + 0.55 * gate ) * ( 1.0 - axisCoord.z );
	}

	// Glint band with a per-pixel threshold shift (the twinkle).
	float Ice_GlintBand( float3 normalWs, float3 lightDir, float3 viewDir, float thresholdShift )
	{
		float3 h = normalize( lightDir + viewDir );
		float spec = pow( saturate( dot( normalWs, h ) ), g_flGlintPower );

		float s = max( g_flGlintSoftness, 0.005 );
		float thr = saturate( g_flGlintThreshold + thresholdShift );
		return smoothstep( thr - s, thr + s, spec );
	}

	float3 Ice_Glints( float3 normalWs, float3 worldPos, float4 screenPos, float3 viewDir, ToonLight sun, float thresholdShift )
	{
		float3 total = Ice_GlintBand( normalWs, sun.SunDir, viewDir, thresholdShift ) * sun.Lit * sun.SunTint;

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

			total += light.Color * ( Ice_GlintBand( normalWs, light.Direction, viewDir, thresholdShift ) * atten );
		}

		return total * g_vGlintTint * g_flGlintIntensity;
	}

	float Ice_Streaks( float2 uv )
	{
		float wobble = g_tIceNoise.Sample( g_sIceNoise, uv * float2( 0.31, 0.11 ) ).g - 0.5;
		float line = 1.0 - abs( frac( uv.x + wobble * 0.8 ) - 0.5 ) * 2.0;
		float gate = g_tIceNoise.Sample( g_sIceNoise, uv * float2( 0.13, 0.47 ) + 7.31 ).a;
		return line * ( 0.55 + 0.45 * gate );
	}

	float2 Ice_ProjectUv( float3 p, float3 normal, float scale )
	{
		float3 an = abs( normal );
		float2 uv = ( an.z >= max( an.x, an.y ) ) ? p.xy : ( ( an.x >= an.y ) ? p.yz : p.xz );
		return uv / max( scale, 0.001 );
	}

	float2 Ice_PatternUv( float3 p, float3 normal, float scale, float stretch )
	{
		float2 uv = Ice_ProjectUv( p, normal, scale );

		float sa, ca;
		sincos( radians( g_flInteriorAngle ), sa, ca );
		uv = float2( uv.x * ca - uv.y * sa, uv.x * sa + uv.y * ca );

		uv.y /= stretch;
		return uv;
	}

	// The albedo compositor. `pos`, `normal` and `viewDir` are all in ONE frame (world for ice,
	// object for a gem); `normalWs`/`viewDirWs` feed the frame-free fresnel terms.
	float3 Gem_Compose( float3 albedo, float3 pos, float3 normal, float3 viewDir, float3 normalWs, float3 viewDirWs, float3 axisCoord )
	{
		// The ray diving into the stone: straight (ice) or bent by the fake IOR.
		float3 dive = -viewDir;
		if ( g_flRefraction > 0.0 )
		{
			float eta = 1.0 / ( 1.0 + 1.4 * g_flRefraction );
			float3 bent = refract( -viewDir, normal, eta );
			if ( dot( bent, bent ) > 0.0 )
				dive = normalize( bent );
		}

		// -- interior ---------------------------------------------------------------------------------------------------
		if ( g_flInteriorStrength > 0.0 )
		{
			float3 drift = float3( g_flTime * g_flInteriorDrift, g_flTime * g_flInteriorDrift * 0.37, 0.0 );
			float3 deepPos = pos + dive * g_flIceDepth + drift;
			float stretch = max( g_flInteriorStretch, 1.0 );

			float s = max( g_flInteriorSoftness, 0.005 );
			float2 uv1 = Ice_PatternUv( deepPos, normal, g_flInteriorScale, stretch );
			float n1;
			if ( g_flInteriorFollowAxis > 0.5 )
			{
				n1 = Ice_AxisBands( axisCoord, uv1, 0.0 );
			}
			else if ( g_flInteriorStreaks > 0.5 )
			{
				n1 = Ice_Streaks( uv1 );
			}
			else
			{
				n1 = g_tIceNoise.Sample( g_sIceNoise, uv1 ).g;
				n1 = lerp( n1, 1.0 - abs( 2.0 * n1 - 1.0 ), g_flInteriorRidge );
			}
			float mask = smoothstep( g_flInteriorThreshold - s, g_flInteriorThreshold + s, n1 );

			float3 deepPos2 = pos + dive * ( g_flIceDepth * 2.2 ) + drift * 0.6;
			float2 uv2 = Ice_PatternUv( deepPos2, normal, g_flInteriorScale * 0.6, stretch );
			float n2;
			if ( g_flInteriorFollowAxis > 0.5 )
			{
				n2 = 0.0;
			}
			else if ( g_flInteriorStreaks > 0.5 )
			{
				n2 = Ice_Streaks( uv2 + float2( 0.5, 3.7 ) );
			}
			else
			{
				n2 = g_tIceNoise.Sample( g_sIceNoise, uv2 ).g;
				n2 = lerp( n2, 1.0 - abs( 2.0 * n2 - 1.0 ), g_flInteriorRidge );
			}
			mask = saturate( mask + 0.5 * smoothstep( g_flInteriorThreshold - s, g_flInteriorThreshold + s, n2 ) );

			float3 interiorColor = lerp( albedo * g_vInteriorTint, g_vInteriorTint, g_flInteriorReplace );
			albedo = lerp( albedo, interiorColor, mask * g_flInteriorStrength );
		}

		// -- gloss ------------------------------------------------------------------------------------------------------
		if ( g_flGlossStrength > 0.0 )
		{
			float3 floatPos = pos + viewDir * g_flGlossFloat;
			float ng = g_tIceNoise.Sample( g_sIceNoise, Ice_ProjectUv( floatPos, normal, g_flGlossScale ) ).a;

			float fresnel = pow( 1.0 - saturate( dot( normalWs, viewDirWs ) ), max( g_flGlossFresnel, 0.001 ) );
			float weight = ( g_flGlossFresnel <= 0.0 ) ? 1.0 : ( 0.35 + 0.65 * fresnel );

			float gs = max( g_flGlossSoftness, 0.005 );
			float gloss = smoothstep( g_flGlossThreshold - gs, g_flGlossThreshold + gs, ng * weight );

			albedo = lerp( albedo, g_vGlossTint, gloss * g_flGlossStrength );
		}

		// -- dark rim (gem) ---------------------------------------------------------------------------------------------
		if ( g_flEdgeStrength > 0.0 )
		{
			float fresnel = pow( 1.0 - saturate( dot( normalWs, viewDirWs ) ), max( g_flEdgePower, 0.1 ) );
			albedo = lerp( albedo, albedo * g_vEdgeTint, fresnel * g_flEdgeStrength );
		}

		return albedo;
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

		float3 normalWs = normalize( i.vNormalWs );
		float3 viewDirWs = normalize( g_vCameraPositionWs.xyz - worldPos );

		// Pattern frame: the world (ice on rocks) or the mesh (a gem in a hand).
		float3 patternPos = worldPos, patternNormal = normalWs, patternView = viewDirWs;
		if ( g_flPatternObjectSpace > 0.5 )
		{
			patternPos = i.vDetailPosOs;
			patternNormal = normalize( i.vDetailNormalOs );
			patternView = normalize( i.vViewDirOs );
		}

		albedo = Gem_Compose( albedo, patternPos, patternNormal, patternView, normalWs, viewDirWs, i.vVertexColor.rgb );

		albedo = Detail_ApplyAlbedo( albedo,
			Detail_FramePos( worldPos, i.vDetailPosOs ),
			Detail_FrameNormal( normalWs, i.vDetailNormalOs ) );

		ToonLight sun = Toon_EvaluateSun( normalWs, worldPos, i.vPositionSs );

		float4 debugColor;
		if ( Toon_Debug( albedo, sun, normalWs, worldPos, debugColor ) )
			return debugColor;

		float3 color = Toon_Shade( albedo, sun, normalWs, worldPos, i.vPositionSs );

		// Light-like additions after the shade, fogged by difference (see heden_ice).
		float3 add = float3( 0.0, 0.0, 0.0 );

		if ( g_flGlintIntensity > 0.0 )
		{
			// Twinkle: the catch threshold wanders per facet with a slow noise crawl.
			float shift = 0.0;
			if ( g_flTwinkle > 0.0 )
			{
				float2 tuv = Ice_ProjectUv( patternPos, patternNormal, max( g_flInteriorScale, 1.0 ) ) * 0.7
					+ g_flTime * g_flTwinkleSpeed * float2( 0.13, 0.07 );
				shift = ( g_tIceNoise.Sample( g_sIceNoise, tuv ).g - 0.5 ) * g_flTwinkle * 0.6;
			}
			add += Ice_Glints( normalWs, worldPos, i.vPositionSs, viewDirWs, sun, shift );
		}

		if ( g_flCoreGlow > 0.0 )
		{
			float centre = pow( saturate( dot( normalWs, viewDirWs ) ), max( g_flCoreFocus, 0.1 ) );
			add += g_vCoreColor * ( centre * g_flCoreGlow );
		}

		if ( any( add > 0.0 ) )
		{
			color += Landmark_Fog( worldPos, i.vPositionSs.xy, add )
				- Landmark_Fog( worldPos, i.vPositionSs.xy, float3( 0.0, 0.0, 0.0 ) );
		}

		return float4( color, 1.0 );
	}
}
