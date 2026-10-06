//=========================================================================================================================
// HEDEN — style test rig: triplanar albedo detail, band wobble, final-color crush
//
// Included by heden_toon_core.hlsl, so every core consumer (toon, foliage, ice, terrain) gets
// EVERYTHING unconditionally — no F_DETAIL_TEXTURE combo any more: the style is adopted
// game-wide, and the combo was doubling every consumer's permutation count. A material opts
// out by leaving the texture unset (mid-gray default = no-op) or zeroing Strength/Wobble.
//
// Defaults are the ADOPTED base style (user-tuned Aug 2026): crush 64 @ hue-preserve 0.5, detail scale 32,
// detail strength 0.05, wobble 0.01 @ scale 16. Set any knob to 0 on a material to opt out.
//=========================================================================================================================

#ifndef HEDEN_STYLE_DETAIL_HLSL
#define HEDEN_STYLE_DETAIL_HLSL

//-------------------------------------------------------------------------------------------------------------------------
// Final-color crush (posterize) — available on every material whose shader includes the toon core.
//
// Applied to the FINAL shaded color (albedo x bands x darkness x dynamic lights), before fog, so
// texture detail and light gradients shatter into flat islands together — the whole point of the
// style. 0/1 = off. 2..64 = number of levels per channel.
//-------------------------------------------------------------------------------------------------------------------------
float g_flCrushLevels < Default( 64.0 ); Range( 0.0, 64.0 ); UiGroup( "Style Test,35/10" ); >;

// 0 = classic per-channel posterize: R/G/B snap independently, so low level counts shift hues
// (the Mortal Sin grunge). 1 = quantize only the brightness and rescale the color proportionally:
// same flat islands, but the hue can never drift off the authored palette.
float g_flCrushHuePreserve < Default( 0.5 ); Range( 0.0, 1.0 ); UiGroup( "Style Test,35/15" ); >;

float3 Crush_Apply( float3 color )
{
	float levels = round( g_flCrushLevels );
	if ( levels < 2.0 )
		return color;

	// Quantize only the displayable 0..1 range; overbright (rim, highlight, rune glow) rides on
	// top untouched so bloom feeds keep working.
	float3 c = min( color, 1.0 );
	float3 over = color - c;

	// Quantize in gamma space: linear-space steps bunch all the levels in the brights and leave
	// the dark half of the image nearly unbanded. Gamma steps are perceptually even.
	float3 g = pow( max( c, 0.0 ), 1.0 / 2.2 );
	float3 perChannel = pow( floor( g * ( levels - 1.0 ) + 0.5 ) / ( levels - 1.0 ), 2.2 );

	// Hue-preserving variant: quantize the max channel only, scale the rest along with it.
	float v = max( c.r, max( c.g, c.b ) );
	float vg = pow( v, 1.0 / 2.2 );
	float vq = pow( floor( vg * ( levels - 1.0 ) + 0.5 ) / ( levels - 1.0 ), 2.2 );
	float3 huePreserved = ( v > 1e-5 ) ? c * ( vq / v ) : float3( vq, vq, vq );

	return lerp( perChannel, huePreserved, g_flCrushHuePreserve ) + over;
}

//-------------------------------------------------------------------------------------------------------------------------
// Triplanar detail texture + band wobble.
//-------------------------------------------------------------------------------------------------------------------------

	// Grayscale, mid-gray (0.5) = no effect. Authored tileable; mips are the built-in
	// distance-shimmer fix — never replace this with procedural noise math. BC7 (not BC4:
	// the compiler rejects BC4 as an OutputFormat — "Invalid output format" at compile).
	CreateInputTexture2D( DetailTexture, Linear, 8, "", "_detail", "Style Test,35/20", Default( 0.5 ) );
	Texture2D g_tStyleDetail < Channel( R, Box( DetailTexture ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	SamplerState g_sStyleDetailSampler < Filter( ANISOTROPIC ); AddressU( WRAP ); AddressV( WRAP ); >;

	// World units per texture tile. Calibrate against known sizes: player = 64u, tree = 57.5u.
	float g_flDetailScale < Default( 32.0 ); Range( 4.0, 512.0 ); UiGroup( "Style Test,35/30" ); >;

	// How hard the texture modulates albedo. 0 = off. At 1, black texels halve-ish / white texels
	// lift-ish the color (multiplier spans 0..2 around the 0.5 gray midpoint).
	float g_flDetailStrength < Default( 0.05 ); Range( 0.0, 1.0 ); UiGroup( "Style Test,35/40" ); >;

	// 0 = world-space projection (seamless across modular architecture — walls, floors).
	// 1 = OBJECT-space projection: the pattern rides the model. REQUIRED for anything that
	// moves (held weapons, rolling debris, characters) or the world-glued noise visibly
	// swims against the motion. Standalone props lose nothing by turning this on.
	float g_flDetailObjectSpace < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Style Test,35/45" ); >;

	// Sun terminator wobble: the detail texture (at its own scale) offsets the band threshold.
	// 0 = off. This is amplitude in half-Lambert units — 0.1 is already a very wavy line.
	float g_flDetailWobble < Default( 0.01 ); Range( 0.0, 0.25 ); UiGroup( "Style Test,35/50" ); >;
	float g_flDetailWobbleScale < Default( 16.0 ); Range( 4.0, 512.0 ); UiGroup( "Style Test,35/60" ); >;

	// Frame selection happens at the MainPs call site (Detail_FramePos/Normal below), NOT via
	// mutable static globals — s&box's shader parser REJECTS `static float3 x = ...;` at
	// global scope (ANTLR Mismatched Token; `static const` is fine, mutable static is not)
	// and the failed parse silently serves the stale .shader_c. The wobble inside the toon
	// core keeps the world frame on purpose: threading the object frame through
	// Toon_EvaluateSun/Toon_Shade isn't worth it for a 0.02-amplitude lighting edge.
	float3 Detail_FramePos( float3 worldPos, float3 posOs )
	{
		return ( g_flDetailObjectSpace > 0.5 ) ? posOs : worldPos;
	}

	float3 Detail_FrameNormal( float3 normalWs, float3 normalOs )
	{
		return ( g_flDetailObjectSpace > 0.5 ) ? normalize( normalOs ) : normalWs;
	}

	// World-space triplanar sample: no UVs needed, so it lands on every prop identically.
	// Weights sharpened so corners blend over a short seam instead of a long smear.
	float Detail_SampleTriplanar( float3 worldPos, float3 normalWs, float scale )
	{
		float3 uvw = worldPos / max( scale, 1.0 );

		float3 w = abs( normalWs );
		w = w * w * w * w;
		w /= max( w.x + w.y + w.z, 1e-5 );

		float x = g_tStyleDetail.Sample( g_sStyleDetailSampler, uvw.yz ).r;
		float y = g_tStyleDetail.Sample( g_sStyleDetailSampler, uvw.xz ).r;
		float z = g_tStyleDetail.Sample( g_sStyleDetailSampler, uvw.xy ).r;

		return x * w.x + y * w.y + z * w.z;
	}

	// Callers pass whichever frame they want the pattern glued to (world for architecture,
	// object for movables — pick with Detail_FramePos/Normal above).
	float3 Detail_ApplyAlbedo( float3 albedo, float3 pos, float3 nrm )
	{
		if ( g_flDetailStrength <= 0.0 )
			return albedo;

		float d = Detail_SampleTriplanar( pos, nrm, g_flDetailScale );
		return max( albedo * ( 1.0 + ( d - 0.5 ) * 2.0 * g_flDetailStrength ), 0.0 );
	}

	// Signed threshold offset for the sun terminator, in half-Lambert units.
	// Always fed the WORLD frame by the toon core (see the frame-selection note above).
	float Detail_BandWobble( float3 worldPos, float3 normalWs )
	{
		if ( g_flDetailWobble <= 0.0 )
			return 0.0;

		float d = Detail_SampleTriplanar( worldPos, normalWs, g_flDetailWobbleScale );
		return ( d - 0.5 ) * 2.0 * g_flDetailWobble;
	}

#endif // HEDEN_STYLE_DETAIL_HLSL
