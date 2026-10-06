//=========================================================================================================================
// HEDEN — toon ice shader (coldvein ore)
//
// heden_toon's shading pipeline (bands/ramp, darkness zones, dynamic lights, fog — via
// heden_toon_core.hlsl) with a FAKE INTERIOR DEPTH layer composed into the albedo first:
//
//   1. Interior blobs — darker shapes sampled at a virtual point BEHIND the surface
//      (worldPos continued along the view ray), so they slide relative to the surface as
//      the camera moves and read as "inside the ice". Quantized into hard toon bands.
//   2. Gloss streaks — painterly near-white shapes floated slightly IN FRONT of the
//      surface (opposite slide), weighted toward silhouettes by fresnel. The counter-motion
//      of the two layers is what sells the "only the outer layer is transparent" look.
//
// Everything happens in ALBEDO space before Toon_Shade, so the effect inherits sun bands,
// fire-light bands, the darkness-zone crush and fog exactly like every other toon surface —
// in a pitch-black mine it is simply dark, never self-lit.
//
// Noise source: the water system's baked tiling noise (materials/water_noise_01.png), bound
// by the vmat. Different channels carry different noise characters — interior reads .g,
// gloss reads .a. Projection is dominant-axis planar in WORLD space (mesh UVs are atlas
// color cells and unusable for patterns); each flat facet picks one stable axis, which is
// exactly right for faceted shards.
//
// A dedicated shader rather than a heden_toon feature: no variant multiplication on the
// 5.6MB toon compile, zero risk to its thirteen rim users.
//=========================================================================================================================

HEADER
{
	Description = "HEDEN toon ice — fake interior depth + gloss streaks over toon shading";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_USE_TEXTURE, 0..1, "Albedo" );
	Feature( F_USE_RAMP, 0..1, "Toon Lighting" );
	// Style-detail is a runtime knob set, not a feature — see heden_toon's permutation note.
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

		// Same rule as heden_toon: landmark objects need their clip position taken through
		// the far-plane squash — a no-op for everything else.
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
	// Ice — noise source
	//---------------------------------------------------------------------------------------------------------------------
	CreateInputTexture2D( IceNoise, Linear, 8, "", "_noise", "Ice,40/5", Default4( 0.5, 0.5, 0.5, 0.5 ) );
	Texture2D g_tIceNoise < Channel( RGBA, Box( IceNoise ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( false ); >;
	SamplerState g_sIceNoise < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); >;

	//---------------------------------------------------------------------------------------------------------------------
	// Ice — interior blobs (the "undeep translucency": darker shapes behind the surface)
	//---------------------------------------------------------------------------------------------------------------------
	// How far behind the surface the shapes sit, in world units. This is the whole illusion:
	// bigger = more slide as the camera moves = reads deeper.
	float g_flIceDepth < Default( 8.0 ); Range( 0.0, 40.0 ); UiGroup( "Ice Interior,41/10" ); >;

	// World units per noise repeat — blob size.
	float g_flInteriorScale < Default( 48.0 ); Range( 4.0, 256.0 ); UiGroup( "Ice Interior,41/20" ); >;

	// Multiplier on the albedo inside a blob: darker and bluer than the surface color, never
	// a replacement, so the atlas hue survives.
	float3 g_vInteriorTint < UiType( Color ); Default3( 0.55, 0.72, 1.0 ); UiGroup( "Ice Interior,41/30" ); >;

	float g_flInteriorStrength < Default( 0.6 ); Range( 0.0, 1.0 ); UiGroup( "Ice Interior,41/40" ); >;

	// Where the noise cuts into a blob, and how hard the cut is — the toon knobs. Low
	// softness = crisp cartoon shapes like the reference.
	float g_flInteriorThreshold < Default( 0.55 ); Range( 0.0, 1.0 ); UiGroup( "Ice Interior,41/50" ); >;
	float g_flInteriorSoftness < Default( 0.08 ); Range( 0.005, 0.5 ); UiGroup( "Ice Interior,41/60" ); >;

	// The three knobs that separate ORES from each other. All default to coldvein's exact
	// behavior — a material that never touches them is pure ice.
	//
	// Shape: 0 = round blobby shapes (ice), 1 = the noise ridge-transformed into thin
	// branching filaments (solarium's ember veins).
	float g_flInteriorRidge < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Ice Interior,41/70" ); >;

	// Value direction: 0 = the interior DARKENS the surface (tint multiplies — dark shapes
	// inside pale ice), 1 = the interior pulls toward the tint's own color, letting the
	// inside be BRIGHTER than the crust (hot veins inside dark solarium). Still albedo:
	// bright never means lit — the darkness crush applies as always, an ore is not redstone.
	float g_flInteriorReplace < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Ice Interior,41/80" ); >;

	// World units per second the interior pattern crawls. Lava-lamp slow is the intent —
	// alive, never flame. 0 = frozen solid (ice).
	float g_flInteriorDrift < Default( 0.0 ); Range( 0.0, 8.0 ); UiGroup( "Ice Interior,41/90" ); >;

	// Anisotropy: how much the interior pattern is elongated along the projection's
	// vertical axis. 1 = round shapes (ice). In streak mode this is how smoothly the
	// crack lines wobble along their length (higher = straighter, lazier waves).
	float g_flInteriorStretch < Default( 1.0 ); Range( 1.0, 12.0 ); UiGroup( "Ice Interior,41/95" ); >;

	// Pattern source: 0 = the noise texture (blobby shapes, optionally ridged — ice).
	// 1 = a generated CRACK pattern: evenly spaced thin lines running up the facet,
	// noise-wobbled so they fracture naturally and broken into segments so they read as
	// cracks, not rails. InteriorScale becomes the spacing between cracks in world units.
	float g_flInteriorStreaks < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Ice Interior,41/97" ); >;

	// Rotates the pattern on the surface, in degrees. 0 = lines run UP the facet (cracks
	// along a crystal's length); 90 = ACROSS it (tiger bands wrapping a prism — solarium).
	// The stretch always applies along the line direction, whatever the angle.
	float g_flInteriorAngle < Default( 0.0 ); Range( 0.0, 180.0 ); UiGroup( "Ice Interior,41/98" ); >;

	// Stripes from the MESH's own baked coordinates instead of any projection: OreForge
	// bakes along-the-length (red) and around-the-girth (green) into vertex color, so the
	// pattern knows each prism's own frame no matter how it points — the thing no
	// world-space projection can do. Stripes run ALONG the length: constant angle, the
	// green channel, wavering with the red as they travel. Needs the baked export.
	float g_flInteriorFollowAxis < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Ice Interior,41/99" ); >;

	// Stripe count AROUND the prism in axis mode (InteriorScale is unused there). Snapped
	// to a whole number in the shader — fractional counts would seam where the angle wraps.
	float g_flInteriorBands < Default( 4.0 ); Range( 1.0, 16.0 ); UiGroup( "Ice Interior,41/100" ); >;

	// Lengthwise stripes: distributed around the girth (angle), wavering along the length,
	// broken into patches by the projected noise so they read as markings, not paint rails.
	float Ice_AxisBands( float3 axisCoord, float2 uv, float phase )
	{
		float wobble = g_tIceNoise.Sample( g_sIceNoise, uv * float2( 0.31, 0.11 ) ).g - 0.5;
		float bands = max( round( g_flInteriorBands ), 1.0 );
		float line = 1.0 - abs( frac( axisCoord.y * bands + phase + wobble * 0.4 ) - 0.5 ) * 2.0;

		// Heavier breakup than the crack pattern — tiger markings taper and split.
		float gate = g_tIceNoise.Sample( g_sIceNoise, uv * float2( 0.13, 0.47 ) + 7.31 ).a;

		// Fade out on the prism's end caps (blue channel), where the angular coordinate
		// degenerates into radial swirls — the wood-grain artifact.
		return line * ( 0.45 + 0.55 * gate ) * ( 1.0 - axisCoord.z );
	}

	//---------------------------------------------------------------------------------------------------------------------
	// Ice — gloss streaks (painterly near-white shapes riding the outer layer)
	//---------------------------------------------------------------------------------------------------------------------
	float3 g_vGlossTint < UiType( Color ); Default3( 0.95, 0.99, 1.0 ); UiGroup( "Ice Gloss,42/10" ); >;
	float g_flGlossStrength < Default( 0.85 ); Range( 0.0, 1.0 ); UiGroup( "Ice Gloss,42/20" ); >;
	float g_flGlossScale < Default( 64.0 ); Range( 4.0, 256.0 ); UiGroup( "Ice Gloss,42/30" ); >;

	// Higher threshold = sparser, smaller streaks.
	float g_flGlossThreshold < Default( 0.62 ); Range( 0.0, 1.0 ); UiGroup( "Ice Gloss,42/40" ); >;
	float g_flGlossSoftness < Default( 0.06 ); Range( 0.005, 0.5 ); UiGroup( "Ice Gloss,42/50" ); >;

	// Biases the streaks toward silhouettes and corners (where painted ice highlights live).
	// 0 spreads them evenly.
	float g_flGlossFresnel < Default( 1.5 ); Range( 0.0, 6.0 ); UiGroup( "Ice Gloss,42/60" ); >;

	// Units the gloss layer floats ABOVE the surface — it counter-slides against the
	// interior, which is what separates "outer layer" from "inside".
	float g_flGlossFloat < Default( 3.0 ); Range( 0.0, 15.0 ); UiGroup( "Ice Gloss,42/70" ); >;

	//---------------------------------------------------------------------------------------------------------------------
	// Ice — glints (hard toon specular catches from the sun and nearby dynamic lights)
	//---------------------------------------------------------------------------------------------------------------------
	// Additive LIGHT response, not albedo: a facet angled between the viewer and a light
	// throws one crisp near-white catch, scaled by that light's attenuation and shadow. A
	// flickering mine fire makes the sparkles dance; with no light there is nothing — the
	// no-self-glow rule holds by construction.
	float3 g_vGlintTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Ice Glint,43/10" ); >;

	// 0 = the whole pass is skipped.
	float g_flGlintIntensity < Default( 0.8 ); Range( 0.0, 3.0 ); UiGroup( "Ice Glint,43/20" ); >;

	// Specular exponent — higher = smaller, tighter catches.
	float g_flGlintPower < Default( 24.0 ); Range( 2.0, 128.0 ); UiGroup( "Ice Glint,43/30" ); >;

	// The toon snap: where the raw specular cuts into a catch, and how hard the edge is.
	float g_flGlintThreshold < Default( 0.5 ); Range( 0.0, 1.0 ); UiGroup( "Ice Glint,43/40" ); >;
	float g_flGlintSoftness < Default( 0.08 ); Range( 0.005, 0.5 ); UiGroup( "Ice Glint,43/50" ); >;

	float Ice_GlintBand( float3 normalWs, float3 lightDir, float3 viewDir )
	{
		float3 h = normalize( lightDir + viewDir );
		float spec = pow( saturate( dot( normalWs, h ) ), g_flGlintPower );

		float s = max( g_flGlintSoftness, 0.005 );
		return smoothstep( g_flGlintThreshold - s, g_flGlintThreshold + s, spec );
	}

	float3 Ice_Glints( float3 normalWs, float3 worldPos, float4 screenPos, float3 viewDir, ToonLight sun )
	{
		// Sun catch: gated by the banded lit factor, so glints obey the same cel terminator
		// and cast shadows as the surface itself.
		float3 total = Ice_GlintBand( normalWs, sun.SunDir, viewDir ) * sun.Lit * sun.SunTint;

		// Dynamic lights: the toon core's own loop shape (culled per-pixel bin, skip
		// directionals), each catch riding that light's color, falloff and shadow.
		uint lightCount = Light::Count( screenPos );

		[loop]
		for ( uint li = 0; li < lightCount; li++ )
		{
			Light light = Light::From( worldPos, screenPos, li );

			if ( light.LightData.Type == LightType::LightTypeDirectional )
				continue;
			if ( !light.LightData.IsDiffuseEnabled() )
				continue;

			// Facing gate first: half-vector specular can graze backfaces, and a glint on
			// the dark side of a facet reads as a bug.
			if ( dot( normalWs, light.Direction ) <= 0.0 )
				continue;

			float atten = light.Attenuation * light.Visibility;
			if ( atten <= 0.0 )
				continue;

			total += light.Color * ( Ice_GlintBand( normalWs, light.Direction, viewDir ) * atten );
		}

		return total * g_vGlintTint * g_flGlintIntensity;
	}

	// Generated crack pattern: parallel lines across the projected horizontal, running up
	// the facet's vertical, wobbled sideways by one noise tap (slow along the length) and
	// gated by a second (faster along the length) so each line breaks into crack segments.
	// uv arrives already divided by InteriorScale, so 1 uv-unit = one crack spacing, and
	// with the vertical pre-compressed by the stretch so the wobble waves are lazy.
	float Ice_Streaks( float2 uv )
	{
		float wobble = g_tIceNoise.Sample( g_sIceNoise, uv * float2( 0.31, 0.11 ) ).g - 0.5;
		float line = 1.0 - abs( frac( uv.x + wobble * 0.8 ) - 0.5 ) * 2.0;

		float gate = g_tIceNoise.Sample( g_sIceNoise, uv * float2( 0.13, 0.47 ) + 7.31 ).a;
		return line * ( 0.55 + 0.45 * gate );
	}

	// Dominant-axis planar projection of a world point: stable per facet, no unusable mesh
	// UVs involved. The facet's own normal picks the plane.
	float2 Ice_ProjectUv( float3 p, float3 normalWs, float scale )
	{
		float3 an = abs( normalWs );
		float2 uv = ( an.z >= max( an.x, an.y ) ) ? p.xy : ( ( an.x >= an.y ) ? p.yz : p.xz );
		return uv / max( scale, 0.001 );
	}

	// Pattern-space UV: project, rotate by the pattern angle, then compress the line axis
	// by the stretch. Angle 0 is a pure pass-through, so ice materials are untouched.
	float2 Ice_PatternUv( float3 p, float3 normalWs, float scale, float stretch )
	{
		float2 uv = Ice_ProjectUv( p, normalWs, scale );

		float sa, ca;
		sincos( radians( g_flInteriorAngle ), sa, ca );
		uv = float2( uv.x * ca - uv.y * sa, uv.x * sa + uv.y * ca );

		uv.y /= stretch;
		return uv;
	}

	// The albedo compositor — everything the ice look adds happens here, pre-lighting.
	float3 Ice_Compose( float3 albedo, float3 worldPos, float3 normalWs, float3 axisCoord )
	{
		float3 viewDir = normalize( g_vCameraPositionWs.xyz - worldPos );

		// -- interior: sample where the view ray would be at depth, quantize, recolor ------------------------------------
		if ( g_flInteriorStrength > 0.0 )
		{
			// The drift crawls the pattern through the stone on two axes at unequal rates,
			// so the repeat never reads as a loop. Zero for ice — frozen means frozen.
			float3 drift = float3( g_flTime * g_flInteriorDrift,
				g_flTime * g_flInteriorDrift * 0.37, 0.0 );

			float3 deepPos = worldPos - viewDir * g_flIceDepth + drift;

			// The stretch compresses the projected second axis — world Z on side-facing
			// facets — so features elongate along it: crack lines up the crystal's length.
			float stretch = max( g_flInteriorStretch, 1.0 );

			float s = max( g_flInteriorSoftness, 0.005 );
			float2 uv1 = Ice_PatternUv( deepPos, normalWs, g_flInteriorScale, stretch );
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
				// Ridge transform: peaks the noise where it crosses its midline, which
				// turns round blobs into thin branching filaments once thresholded.
				n1 = lerp( n1, 1.0 - abs( 2.0 * n1 - 1.0 ), g_flInteriorRidge );
			}
			float mask = smoothstep( g_flInteriorThreshold - s, g_flInteriorThreshold + s, n1 );

			// A second, deeper and finer layer at half weight — two depths parallaxing at
			// different rates is what makes the inside read as a volume, not a decal. In
			// streak mode it is a half-phase-offset set of finer cracks behind the first.
			float3 deepPos2 = worldPos - viewDir * ( g_flIceDepth * 2.2 ) + drift * 0.6;
			float2 uv2 = Ice_PatternUv( deepPos2, normalWs, g_flInteriorScale * 0.6, stretch );
			float n2;
			if ( g_flInteriorFollowAxis > 0.5 )
			{
				// Axis mode is a SURFACE MARKING: exactly two tones. The depth-offset
				// second layer half-darkens its overlaps into a third tone and the
				// phase offset draws chevrons where the sets meet — both wrong here.
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

			// Darken (multiply, ice) or recolor outright (veins brighter than the crust,
			// solarium) — both stay albedo, both get crushed by darkness like any rock.
			float3 interiorColor = lerp( albedo * g_vInteriorTint, g_vInteriorTint, g_flInteriorReplace );
			albedo = lerp( albedo, interiorColor, mask * g_flInteriorStrength );
		}

		// -- gloss: floated above the surface, fresnel-weighted, quantized near-white ------------------------------------
		if ( g_flGlossStrength > 0.0 )
		{
			float3 floatPos = worldPos + viewDir * g_flGlossFloat;
			float ng = g_tIceNoise.Sample( g_sIceNoise, Ice_ProjectUv( floatPos, normalWs, g_flGlossScale ) ).a;

			float fresnel = pow( 1.0 - saturate( dot( normalWs, viewDir ) ), max( g_flGlossFresnel, 0.001 ) );
			float weight = ( g_flGlossFresnel <= 0.0 ) ? 1.0 : ( 0.35 + 0.65 * fresnel );

			float gs = max( g_flGlossSoftness, 0.005 );
			float gloss = smoothstep( g_flGlossThreshold - gs, g_flGlossThreshold + gs, ng * weight );

			albedo = lerp( albedo, g_vGlossTint, gloss * g_flGlossStrength );
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

		// The ice look is albedo work: composed BEFORE lighting so bands, fire lights,
		// darkness zones and fog treat it like any other toon surface. The vertex color's
		// red channel carries OreForge's baked along-the-prism coordinate (axis mode).
		albedo = Ice_Compose( albedo, worldPos, normalWs, i.vVertexColor.rgb );

		// Style detail: grain over the COMPOSED ice (interior + gloss included), so ice
		// wears the same surface filter as every other material in the world.
		albedo = Detail_ApplyAlbedo( albedo,
			Detail_FramePos( worldPos, i.vDetailPosOs ),
			Detail_FrameNormal( normalWs, i.vDetailNormalOs ) );

		ToonLight sun = Toon_EvaluateSun( normalWs, worldPos, i.vPositionSs );

		float4 debugColor;
		if ( Toon_Debug( albedo, sun, normalWs, worldPos, debugColor ) )
			return debugColor;

		float3 color = Toon_Shade( albedo, sun, normalWs, worldPos, i.vPositionSs );

		// Glints add AFTER the shade (they are light, so the darkness crush must not eat
		// them — same rule the core's dynamic diffuse follows) but still under fog:
		// Toon_Shade already fogged the surface, so the addition is fogged by difference —
		// fog(glint) - fog(black) = glint * (1 - fogAmount), exactly the contribution a
		// pre-fog add would have made.
		if ( g_flGlintIntensity > 0.0 )
		{
			float3 viewDir = normalize( g_vCameraPositionWs.xyz - worldPos );
			float3 glint = Ice_Glints( normalWs, worldPos, i.vPositionSs, viewDir, sun );

			color += Landmark_Fog( worldPos, i.vPositionSs.xy, glint )
				- Landmark_Fog( worldPos, i.vPositionSs.xy, float3( 0.0, 0.0, 0.0 ) );
		}

		return float4( color, 1.0 );
	}
}
