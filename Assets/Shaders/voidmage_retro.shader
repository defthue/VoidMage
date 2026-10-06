// VOID MAGE retro filter — a single fullscreen grab+blit that stacks every retro
// technique we want to trial, each independently switchable and tunable from the
// RetroFilter component (Code/Rendering/RetroFilter.cs).
//
// Pipeline per OUTPUT pixel:
//   1. CRT curvature warps the lookup uv (black outside the tube).
//   2. The uv is snapped onto a virtual low-res grid (480p, 360p, ...). Each virtual
//      pixel is shaded ONCE by ShadeLowResPixel, so quantisation, dither and edges
//      all happen on the low-res grid — the way real hardware would have done it.
//   3. Per virtual pixel: scene sample (point / box / supersampled, optional
//      chromatic aberration) → depth fog (optionally banded like Doom's colormap)
//      → depth edge ink → colour grading → colour depth reduction with dither.
//   4. Upscale back to the output grid (nearest = blocky, smooth = bilinear CRT-ish).
//   5. Output-grid overlays: scanlines, aperture / slot mask, pixel grid, vignette.
//   6. Wet/dry blend with the untouched frame and an A/B split for comparing.
//
// Everything reads the frame through Load() at integer pixel coordinates, so no
// sampler state can smear a virtual pixel across its neighbours.

HEADER
{
	Description = "VOID MAGE retro filter — low-res, dither, palette, fog, CRT";
}

MODES
{
	Default();
	Forward();
}

FEATURES
{
}

COMMON
{
	#include "postprocess/shared.hlsl"
}

struct VertexInput
{
	float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
	float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PixelInput
{
	float2 uv : TEXCOORD0;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif

	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

VS
{
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o;
		o.vPositionPs = float4( i.vPositionOs.xy, 0.0f, 1.0f );
		o.uv = i.vTexCoord;
		return o;
	}
}

PS
{
	#include "postprocess/common.hlsl"
	#include "common/classes/Depth.hlsl"

	Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;
	Texture2D g_tRetroPalette < Attribute( "RetroPalette" ); SrgbRead( true ); >;

	// ---- Master -------------------------------------------------------------------
	float g_flRetroBlend < Attribute( "RetroBlend" ); Default( 1.0 ); >;
	float g_flRetroSplit < Attribute( "RetroSplit" ); Default( 0.0 ); >;

	// ---- Resolution ---------------------------------------------------------------
	float g_flRetroResHeight < Attribute( "RetroResHeight" ); Default( 0.0 ); >;        // 0 = native
	float g_flRetroPixelAspect < Attribute( "RetroPixelAspect" ); Default( 1.0 ); >;
	float g_flRetroSampleQuality < Attribute( "RetroSampleQuality" ); Default( 0.0 ); >; // 0 point, 1 quad, 2 super
	float g_flRetroUpscale < Attribute( "RetroUpscale" ); Default( 0.0 ); >;             // 0 nearest, 1 smooth
	float g_flRetroAberration < Attribute( "RetroAberration" ); Default( 0.0 ); >;       // full-res px

	// ---- Hit ripple ---------------------------------------------------------------
	// A ring of radial displacement around a screen point (the player's sword hit): the frame is pushed
	// outward just ahead of the ring and pulled back just behind it, a wave the width of RetroShockWidth.
	float g_flRetroShockCenterX < Attribute( "RetroShockCenterX" ); Default( 0.5 ); >;   // uv
	float g_flRetroShockCenterY < Attribute( "RetroShockCenterY" ); Default( 0.5 ); >;   // uv
	float g_flRetroShockRadius < Attribute( "RetroShockRadius" ); Default( 0.0 ); >;     // full-res px
	float g_flRetroShockWidth < Attribute( "RetroShockWidth" ); Default( 26.0 ); >;      // full-res px
	float g_flRetroShockStrength < Attribute( "RetroShockStrength" ); Default( 0.0 ); >; // full-res px, 0 = off

	// ---- Depth fog ----------------------------------------------------------------
	float g_flRetroFogEnabled < Attribute( "RetroFogEnabled" ); Default( 0.0 ); >;
	float3 g_vRetroFogColor < Attribute( "RetroFogColor" ); Default3( 0.0, 0.0, 0.0 ); >;
	float g_flRetroFogStart < Attribute( "RetroFogStart" ); Default( 200.0 ); >;
	float g_flRetroFogEnd < Attribute( "RetroFogEnd" ); Default( 1500.0 ); >;
	float g_flRetroFogSteps < Attribute( "RetroFogSteps" ); Default( 0.0 ); >;
	float g_flRetroFogCurve < Attribute( "RetroFogCurve" ); Default( 1.0 ); >;
	float g_flRetroFogMax < Attribute( "RetroFogMax" ); Default( 1.0 ); >;
	float g_flRetroFogSky < Attribute( "RetroFogSky" ); Default( 1.0 ); >;
	float g_flRetroFogOnly < Attribute( "RetroFogOnly" ); Default( 0.0 ); >;
	float g_flRetroFogSeal < Attribute( "RetroFogSeal" ); Default( 1.0 ); >;

	// The tube-only pass after the UI: just the CRT curvature over the finished frame, HUD included.
	float g_flRetroCurveOnly < Attribute( "RetroCurveOnly" ); Default( 0.0 ); >;

	// ---- Depth edges --------------------------------------------------------------
	float g_flRetroEdgeEnabled < Attribute( "RetroEdgeEnabled" ); Default( 0.0 ); >;
	float3 g_vRetroEdgeColor < Attribute( "RetroEdgeColor" ); Default3( 0.0, 0.0, 0.0 ); >;
	float g_flRetroEdgeThreshold < Attribute( "RetroEdgeThreshold" ); Default( 0.08 ); >;
	float g_flRetroEdgeStrength < Attribute( "RetroEdgeStrength" ); Default( 1.0 ); >;

	// ---- Grading ------------------------------------------------------------------
	float g_flRetroGradeEnabled < Attribute( "RetroGradeEnabled" ); Default( 0.0 ); >;
	float g_flRetroBrightness < Attribute( "RetroBrightness" ); Default( 1.0 ); >;
	float g_flRetroContrast < Attribute( "RetroContrast" ); Default( 1.0 ); >;
	float g_flRetroSaturation < Attribute( "RetroSaturation" ); Default( 1.0 ); >;
	float g_flRetroGamma < Attribute( "RetroGamma" ); Default( 1.0 ); >;
	float3 g_vRetroTint < Attribute( "RetroTint" ); Default3( 1.0, 1.0, 1.0 ); >;
	float g_flRetroShadowLift < Attribute( "RetroShadowLift" ); Default( 0.0 ); >;
	float3 g_vRetroLiftColor < Attribute( "RetroLiftColor" ); Default3( 0.4, 0.45, 0.6 ); >;
	float g_flRetroLiftRange < Attribute( "RetroLiftRange" ); Default( 0.08 ); >;
	float g_flRetroDuotone < Attribute( "RetroDuotone" ); Default( 0.0 ); >;
	float3 g_vRetroShadowColor < Attribute( "RetroShadowColor" ); Default3( 0.05, 0.02, 0.12 ); >;
	float3 g_vRetroHighlightColor < Attribute( "RetroHighlightColor" ); Default3( 1.0, 0.9, 0.7 ); >;

	// ---- Colour depth + dither ----------------------------------------------------
	float g_flRetroQuantEnabled < Attribute( "RetroQuantEnabled" ); Default( 0.0 ); >;
	float g_flRetroQuantMode < Attribute( "RetroQuantMode" ); Default( 0.0 ); >;         // 0 rgb, 1 luminance, 2 palette
	float3 g_vRetroBits < Attribute( "RetroBits" ); Default3( 5.0, 6.0, 5.0 ); >;
	float g_flRetroLumSteps < Attribute( "RetroLumSteps" ); Default( 8.0 ); >;
	float g_flRetroPaletteCount < Attribute( "RetroPaletteCount" ); Default( 0.0 ); >;
	float g_flRetroPaletteDistance < Attribute( "RetroPaletteDistance" ); Default( 1.0 ); >; // 0 rgb, 1 perceptual
	float g_flRetroQuantGamma < Attribute( "RetroQuantGamma" ); Default( 1.0 ); >;
	float g_flRetroDitherMode < Attribute( "RetroDitherMode" ); Default( 0.0 ); >;       // 0 none 1 b2 2 b4 3 b8 4 ign 5 white
	float g_flRetroDitherStrength < Attribute( "RetroDitherStrength" ); Default( 1.0 ); >;
	float g_flRetroDitherScale < Attribute( "RetroDitherScale" ); Default( 1.0 ); >;
	float g_flRetroDitherAnimate < Attribute( "RetroDitherAnimate" ); Default( 0.0 ); >;
	float g_flRetroDitherLock < Attribute( "RetroDitherLock" ); Default( 0.0 ); >;         // pattern follows the view rotation
	float2 g_vRetroDitherShift < Attribute( "RetroDitherShift" ); Default2( 0.0, 0.0 ); >; // virtual px, from the camera's yaw/pitch

	// ---- CRT ----------------------------------------------------------------------
	float g_flRetroCrtEnabled < Attribute( "RetroCrtEnabled" ); Default( 0.0 ); >;
	float g_flRetroCurvature < Attribute( "RetroCurvature" ); Default( 0.0 ); >;
	float g_flRetroScanStrength < Attribute( "RetroScanStrength" ); Default( 0.0 ); >;
	float g_flRetroScanPitch < Attribute( "RetroScanPitch" ); Default( 0.0 ); >;         // 0 = per low-res row, else output px
	float g_flRetroScanHardness < Attribute( "RetroScanHardness" ); Default( 0.0 ); >;
	float g_flRetroMaskType < Attribute( "RetroMaskType" ); Default( 0.0 ); >;           // 0 none 1 grille 2 slot
	float g_flRetroMaskStrength < Attribute( "RetroMaskStrength" ); Default( 0.0 ); >;
	float g_flRetroGridStrength < Attribute( "RetroGridStrength" ); Default( 0.0 ); >;
	float g_flRetroGridWidth < Attribute( "RetroGridWidth" ); Default( 1.0 ); >;
	float g_flRetroVignetteStrength < Attribute( "RetroVignetteStrength" ); Default( 0.0 ); >;
	float g_flRetroVignetteRadius < Attribute( "RetroVignetteRadius" ); Default( 0.8 ); >;
	float g_flRetroVignetteSoftness < Attribute( "RetroVignetteSoftness" ); Default( 0.6 ); >;
	float g_flRetroVignetteRoundness < Attribute( "RetroVignetteRoundness" ); Default( 0.5 ); >;

	static const float RETRO_PI = 3.14159265;
	static const int MaxPaletteColors = 64;

	// -------------------------------------------------------------------------------
	// Frame access — integer Load only, clamped to the viewport.
	// -------------------------------------------------------------------------------
	float3 FetchPx( float2 px )
	{
		int2 p = clamp( int2( floor( px ) ), int2( 0, 0 ), int2( g_vViewportSize ) - 1 );
		return g_tColorBuffer.Load( int3( p, 0 ) ).rgb;
	}

	// Bilinear fetch from four integer loads, for warps that land between source pixels.
	float3 FetchBilinear( float2 px )
	{
		float2 f = px - 0.5;
		float2 b = floor( f );
		float2 t = f - b;
		float3 c00 = FetchPx( b + float2( 0.5, 0.5 ) );
		float3 c10 = FetchPx( b + float2( 1.5, 0.5 ) );
		float3 c01 = FetchPx( b + float2( 0.5, 1.5 ) );
		float3 c11 = FetchPx( b + float2( 1.5, 1.5 ) );
		return lerp( lerp( c00, c10, t.x ), lerp( c01, c11, t.x ), t.y );
	}

	// Lateral chromatic aberration: red pulled left, blue pushed right, in source px.
	float3 FetchAberrated( float2 px )
	{
		if ( g_flRetroAberration <= 0.001 )
			return FetchPx( px );

		float2 d = float2( g_flRetroAberration, 0.0 );
		return float3( FetchPx( px - d ).r, FetchPx( px ).g, FetchPx( px + d ).b );
	}

	// The scene colour for one virtual pixel whose block spans blockPx source pixels.
	float3 SampleScene( float2 centerPx, float2 blockPx )
	{
		int quality = (int) g_flRetroSampleQuality;

		// A block of one source pixel has nothing to average.
		if ( quality == 0 || all( blockPx <= 1.001 ) )
			return FetchAberrated( centerPx );

		if ( quality == 1 )
		{
			float3 sum = 0;
			[unroll]
			for ( int y = 0; y < 2; y++ )
			{
				[unroll]
				for ( int x = 0; x < 2; x++ )
				{
					float2 o = ( float2( x, y ) - 0.5 ) * 0.5 * blockPx;
					sum += FetchAberrated( centerPx + o );
				}
			}
			return sum * 0.25;
		}

		float3 sum16 = 0;
		[unroll]
		for ( int y2 = 0; y2 < 4; y2++ )
		{
			[unroll]
			for ( int x2 = 0; x2 < 4; x2++ )
			{
				float2 o = ( ( float2( x2, y2 ) + 0.5 ) / 4.0 - 0.5 ) * blockPx;
				sum16 += FetchAberrated( centerPx + o );
			}
		}
		return sum16 / 16.0;
	}

	float2 ClampPx( float2 px )
	{
		return clamp( px, float2( 0.5, 0.5 ), g_vViewportSize - 0.5 );
	}

	// The hit ripple: where this pixel reads its colour from, bent radially around the ring.
	float2 ApplyShockRipple( float2 srcUv )
	{
		if ( g_flRetroShockStrength <= 0.001 )
			return srcUv;
		float2 toPx = ( srcUv - float2( g_flRetroShockCenterX, g_flRetroShockCenterY ) ) * g_vViewportSize;
		float d = length( toPx );
		if ( d < 0.001 )
			return srcUv;
		float x = ( d - g_flRetroShockRadius ) / max( g_flRetroShockWidth, 1.0 );
		float wave = x * exp( -x * x );   // push ahead of the ring, pull behind it, nothing far from it
		float2 dispPx = ( toPx / d ) * wave * g_flRetroShockStrength;
		return saturate( srcUv - dispPx / g_vViewportSize );
	}

	float LinearDepthAt( float2 px )
	{
		return Depth::GetLinear( ClampPx( px ) );
	}

	bool IsSkyAt( float2 px )
	{
		return Depth::GetNormalized( ClampPx( px ) ) >= 0.99999;
	}

	// The depth the fog should use at px: its own, or with RetroFogSeal the farthest of itself and its
	// four neighbours (step = one source px for the full-res pass, one block for the virtual grid).
	// The colour at a silhouette is an anti-aliased blend of the model and what lies behind it, while
	// the depth is the model's alone; fogging by the farther neighbour lets that edge pixel take the
	// background's fog instead of keeping a bright unfogged sliver of it.
	void FogDepthAt( float2 px, float2 step, out float dist, out bool sky )
	{
		dist = LinearDepthAt( px );
		sky = IsSkyAt( px );
		if ( g_flRetroFogSeal < 0.5 )
			return;

		float2 taps[4] = { float2( -step.x, 0 ), float2( step.x, 0 ), float2( 0, -step.y ), float2( 0, step.y ) };
		[unroll]
		for ( int t = 0; t < 4; t++ )
		{
			float2 p = px + taps[t];
			dist = max( dist, LinearDepthAt( p ) );
			sky = sky || IsSkyAt( p );
		}
	}

	// -------------------------------------------------------------------------------
	// Dither patterns, all returning a threshold in (0,1).
	// -------------------------------------------------------------------------------
	float Bayer2( int2 p )
	{
		static const float m[4] = { 0, 2, 3, 1 };
		return ( m[( p.y & 1 ) * 2 + ( p.x & 1 )] + 0.5 ) / 4.0;
	}

	float Bayer4( int2 p )
	{
		static const float m[16] = {
			0, 8, 2, 10,
			12, 4, 14, 6,
			3, 11, 1, 9,
			15, 7, 13, 5 };
		return ( m[( p.y & 3 ) * 4 + ( p.x & 3 )] + 0.5 ) / 16.0;
	}

	float Bayer8( int2 p )
	{
		static const float m[64] = {
			0, 32, 8, 40, 2, 34, 10, 42,
			48, 16, 56, 24, 50, 18, 58, 26,
			12, 44, 4, 36, 14, 46, 6, 38,
			60, 28, 52, 20, 62, 30, 54, 22,
			3, 35, 11, 43, 1, 33, 9, 41,
			51, 19, 59, 27, 49, 17, 57, 25,
			15, 47, 7, 39, 13, 45, 5, 37,
			63, 31, 55, 23, 61, 29, 53, 21 };
		return ( m[( p.y & 7 ) * 8 + ( p.x & 7 )] + 0.5 ) / 64.0;
	}

	// Jimenez interleaved gradient noise — even neighbourhood spread, no tiling.
	float NoiseIgn( float2 p )
	{
		return frac( 52.9829189 * frac( dot( p, float2( 0.06711056, 0.00583715 ) ) ) );
	}

	float NoiseWhite( float2 p )
	{
		return frac( sin( dot( p, float2( 12.9898, 78.233 ) ) ) * 43758.5453 );
	}

	// Signed offset in units of one quantisation step, for the virtual pixel `lp`.
	float DitherOffset( int2 lp )
	{
		int mode = (int) g_flRetroDitherMode;
		if ( mode == 0 )
			return 0.0;

		// View lock (Obra Dinn style): the pattern is slid by the camera's yaw and pitch in
		// virtual pixels, so turning the view carries the pattern with the world instead of
		// dragging the world underneath a fixed grid. Floor division keeps the cells even
		// once the shifted coordinate goes negative.
		if ( g_flRetroDitherLock > 0.5 )
			lp -= int2( round( g_vRetroDitherShift ) );
		int2 cell = int2( floor( float2( lp ) / max( g_flRetroDitherScale, 1.0 ) ) );
		int frame = g_flRetroDitherAnimate > 0.5 ? (int) floor( g_flTime * 30.0 ) : 0;
		int2 cellShift = cell + int2( frame, frame );
		float2 cellF = float2( cell ) + (float) frame * 5.588238;

		float t = 0.5;
		if ( mode == 1 ) t = Bayer2( cellShift );
		else if ( mode == 2 ) t = Bayer4( cellShift );
		else if ( mode == 3 ) t = Bayer8( cellShift );
		else if ( mode == 4 ) t = NoiseIgn( cellF );
		else t = NoiseWhite( cellF );

		return ( t - 0.5 ) * g_flRetroDitherStrength;
	}

	// -------------------------------------------------------------------------------
	// Colour depth reduction.
	// -------------------------------------------------------------------------------
	float3 ToGamma( float3 c ) { return pow( max( c, 0.0 ), 1.0 / 2.2 ); }
	float3 ToLinear( float3 c ) { return pow( max( c, 0.0 ), 2.2 ); }

	float3 NearestPaletteColor( float3 c )
	{
		int count = clamp( (int) g_flRetroPaletteCount, 1, MaxPaletteColors );
		float3 weights = g_flRetroPaletteDistance > 0.5 ? float3( 0.299, 0.587, 0.114 ) * 3.0 : float3( 1, 1, 1 );

		float3 best = 0;
		float bestDist = 1e9;

		[loop]
		for ( int k = 0; k < count; k++ )
		{
			float3 p = g_tRetroPalette.Load( int3( k, 0, 0 ) ).rgb;
			if ( g_flRetroQuantGamma > 0.5 )
				p = ToGamma( p );

			float3 d = ( p - c ) * weights;
			float dist = dot( d, d );
			if ( dist < bestDist )
			{
				bestDist = dist;
				best = p;
			}
		}
		return best;
	}

	float3 Quantize( float3 c, int2 lp )
	{
		float3 q = saturate( c );
		if ( g_flRetroQuantGamma > 0.5 )
			q = ToGamma( q );

		float d = DitherOffset( lp );
		int mode = (int) g_flRetroQuantMode;

		if ( mode == 0 )
		{
			float3 levels = exp2( clamp( g_vRetroBits, 1.0, 8.0 ) ) - 1.0;
			q = round( q * levels + d ) / levels;
		}
		else if ( mode == 1 )
		{
			float steps = max( g_flRetroLumSteps - 1.0, 1.0 );
			float y = GetLuminance( q );
			float yq = round( y * steps + d ) / steps;
			q = saturate( q * ( yq / max( y, 1e-4 ) ) );
		}
		else
		{
			// Dither spread for a palette: roughly one "level" of a cube root of its size.
			float spread = 1.0 / max( pow( max( g_flRetroPaletteCount, 2.0 ), 1.0 / 3.0 ) - 1.0, 1.0 );
			q = NearestPaletteColor( saturate( q + d * spread ) );
		}

		if ( g_flRetroQuantGamma > 0.5 )
			q = ToLinear( q );

		return q;
	}

	// -------------------------------------------------------------------------------
	// Depth fog — Doom's colormap when stepped, PS1 distance haze when smooth. Shared by
	// the low-res pass and the early fog-only pass (RetroFogOnly), which runs right after
	// the opaque world so particles, the slash and spells drawn later are never fogged by
	// the wall behind them.
	// -------------------------------------------------------------------------------
	float3 ApplyRetroFog( float3 c, float dist, bool sky )
	{
		float f = DistanceFalloff( dist, g_flRetroFogStart, g_flRetroFogEnd, max( g_flRetroFogCurve, 0.01 ) );
		if ( sky )
			f = g_flRetroFogSky > 0.5 ? 1.0 : 0.0;

		float steps = round( g_flRetroFogSteps );
		if ( steps >= 1.0 )
			f = round( f * steps ) / steps;

		return lerp( c, g_vRetroFogColor, saturate( f * g_flRetroFogMax ) );
	}

	// -------------------------------------------------------------------------------
	// Everything that happens on the virtual low-res grid.
	// -------------------------------------------------------------------------------
	float3 ShadeLowResPixel( int2 lp, float2 count )
	{
		lp = clamp( lp, int2( 0, 0 ), int2( count ) - 1 );

		float2 blockPx = g_vViewportSize / count;
		float2 centerPx = ( float2( lp ) + 0.5 ) * blockPx;

		float3 c = SampleScene( centerPx, blockPx );

		bool sky = IsSkyAt( centerPx );
		float dist = LinearDepthAt( centerPx );

		if ( g_flRetroFogEnabled > 0.5 )
		{
			float fogDist; bool fogSky;
			FogDepthAt( centerPx, blockPx, fogDist, fogSky );
			c = ApplyRetroFog( c, fogDist, fogSky );
		}

		// Depth edges — ink lines wherever a neighbouring virtual pixel sits further
		// away. Relative to distance so far walls don't ink.
		if ( g_flRetroEdgeEnabled > 0.5 && !sky )
		{
			float dl = LinearDepthAt( centerPx - float2( blockPx.x, 0 ) );
			float dr = LinearDepthAt( centerPx + float2( blockPx.x, 0 ) );
			float du = LinearDepthAt( centerPx - float2( 0, blockPx.y ) );
			float dd = LinearDepthAt( centerPx + float2( 0, blockPx.y ) );

			float e = max( max( dl, dr ), max( du, dd ) ) - dist;
			float rel = e / max( dist, 1.0 );
			float ink = step( g_flRetroEdgeThreshold, rel ) * g_flRetroEdgeStrength;
			c = lerp( c, g_vRetroEdgeColor, saturate( ink ) );
		}

		if ( g_flRetroGradeEnabled > 0.5 )
		{
			c *= g_flRetroBrightness;
			c = ( c - 0.5 ) * g_flRetroContrast + 0.5;
			float y = GetLuminance( c );
			c = lerp( y.xxx, c, g_flRetroSaturation );
			c = pow( max( c, 0.0 ), g_flRetroGamma );
			c *= g_vRetroTint;

			// Black lift: raises pixels darker than LiftRange toward a tinted floor and
			// leaves everything brighter alone, so it never becomes a veil over the
			// mid-tones. Runs after fog, so the fogged-out distance lifts to the same
			// floor and unlit areas read as "dark" instead of "nothing".
			if ( g_flRetroShadowLift > 0.0001 )
			{
				float yLift = GetLuminance( c );
				float w = saturate( 1.0 - yLift / max( g_flRetroLiftRange, 0.001 ) );
				w *= w;
				c += g_vRetroLiftColor * g_flRetroShadowLift * w;
			}

			if ( g_flRetroDuotone > 0.001 )
			{
				float y2 = saturate( GetLuminance( c ) );
				float3 tone = lerp( g_vRetroShadowColor, g_vRetroHighlightColor, y2 );
				c = lerp( c, tone, g_flRetroDuotone );
			}
		}

		if ( g_flRetroQuantEnabled > 0.5 )
			c = Quantize( c, lp );

		return c;
	}

	// -------------------------------------------------------------------------------
	// CRT helpers on the output grid.
	// -------------------------------------------------------------------------------
	float2 Curve( float2 uv )
	{
		float2 p = uv * 2.0 - 1.0;
		float2 q = p;
		q.x *= 1.0 + g_flRetroCurvature * p.y * p.y;
		q.y *= 1.0 + g_flRetroCurvature * p.x * p.x;
		return q * 0.5 + 0.5;
	}

	float3 ApplyCrtOverlays( float3 c, float2 uv, float2 outPx, float2 lpF, float2 blockPx, bool lowRes )
	{
		float aspect = g_vViewportSize.x / g_vViewportSize.y;

		if ( g_flRetroScanStrength > 0.001 )
		{
			float phase = g_flRetroScanPitch < 0.5 ? frac( lpF.y ) : frac( outPx.y / g_flRetroScanPitch );
			float soft = 1.0 - sin( phase * RETRO_PI );
			float hard = step( 0.5, phase );
			float dark = lerp( soft, hard, saturate( g_flRetroScanHardness ) );
			c *= 1.0 - g_flRetroScanStrength * dark;
		}

		int maskType = (int) g_flRetroMaskType;
		if ( maskType > 0 && g_flRetroMaskStrength > 0.001 )
		{
			int col = (int) outPx.x;
			int row = (int) outPx.y;
			if ( maskType == 2 && ( ( row / 3 ) & 1 ) == 1 )
				col += 1;

			int ch = col % 3;
			float3 m = float3( ch == 0 ? 1.0 : 0.0, ch == 1 ? 1.0 : 0.0, ch == 2 ? 1.0 : 0.0 ) * 0.65 + 0.35;
			c *= lerp( float3( 1, 1, 1 ), m, g_flRetroMaskStrength );
		}

		if ( lowRes && g_flRetroGridStrength > 0.001 )
		{
			float2 fb = frac( lpF );
			float2 wf = g_flRetroGridWidth / blockPx;
			float onGrid = ( fb.x < wf.x || fb.y < wf.y ) ? 1.0 : 0.0;
			c *= 1.0 - g_flRetroGridStrength * onGrid;
		}

		if ( g_flRetroVignetteStrength > 0.001 )
		{
			float2 p = uv * 2.0 - 1.0;
			p.x *= lerp( 1.0, aspect, saturate( g_flRetroVignetteRoundness ) );
			float d = length( p );
			float v = 1.0 - smoothstep( g_flRetroVignetteRadius, g_flRetroVignetteRadius + max( g_flRetroVignetteSoftness, 0.01 ), d );
			c *= lerp( 1.0, v, g_flRetroVignetteStrength );
		}

		return c;
	}

	// -------------------------------------------------------------------------------
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = CalculateViewportUv( i.vPositionSs.xy );
		float2 outPx = uv * g_vViewportSize;

		float3 original = FetchPx( outPx );

		// A/B split: everything left of the split line is the untouched frame.
		if ( uv.x < g_flRetroSplit )
			return float4( original, 1.0 );

		// The early fog-only pass: fog at this pixel's own depth, nothing else (the late pass
		// then pixelates the already fogged frame with its own fog switched off).
		if ( g_flRetroFogOnly > 0.5 )
		{
			float fogDist; bool fogSky;
			FogDepthAt( outPx, float2( 1.0, 1.0 ), fogDist, fogSky );
			float3 fogged = ApplyRetroFog( original, fogDist, fogSky );
			return float4( lerp( original, fogged, saturate( g_flRetroBlend ) ), 1.0 );
		}

		// The tube-only pass (after the UI): bend the finished frame, HUD and all, black outside the
		// tube. The world pass ran flat so nothing is bent twice.
		if ( g_flRetroCurveOnly > 0.5 )
		{
			float2 cuv = Curve( uv );
			if ( any( cuv < 0.0 ) || any( cuv > 1.0 ) )
				return float4( lerp( original, float3( 0, 0, 0 ), g_flRetroBlend ), 1.0 );
			float3 bent = FetchBilinear( cuv * g_vViewportSize );
			return float4( lerp( original, bent, saturate( g_flRetroBlend ) ), 1.0 );
		}

		bool crt = g_flRetroCrtEnabled > 0.5;

		float2 srcUv = uv;
		if ( crt && g_flRetroCurvature > 0.0001 )
		{
			srcUv = Curve( uv );
			if ( any( srcUv < 0.0 ) || any( srcUv > 1.0 ) )
				return float4( lerp( original, float3( 0, 0, 0 ), g_flRetroBlend ), 1.0 );
		}
		srcUv = ApplyShockRipple( srcUv );

		// The virtual grid: native when the resolution feature is off.
		bool lowRes = g_flRetroResHeight >= 1.0;
		float aspect = g_vViewportSize.x / g_vViewportSize.y;
		float2 count = g_vViewportSize;
		if ( lowRes )
		{
			float h = round( g_flRetroResHeight );
			float w = max( round( h * aspect / max( g_flRetroPixelAspect, 0.1 ) ), 1.0 );
			count = float2( w, h );
		}
		float2 blockPx = g_vViewportSize / count;

		float2 lpF = srcUv * count;
		float3 c;

		if ( lowRes && g_flRetroUpscale > 0.5 )
		{
			// Smooth upscale: bilinear between the four nearest virtual pixels, each
			// shaded (quantised, dithered) on its own before the blend.
			float2 f = lpF - 0.5;
			int2 b = int2( floor( f ) );
			float2 t = frac( f );
			float3 c00 = ShadeLowResPixel( b, count );
			float3 c10 = ShadeLowResPixel( b + int2( 1, 0 ), count );
			float3 c01 = ShadeLowResPixel( b + int2( 0, 1 ), count );
			float3 c11 = ShadeLowResPixel( b + int2( 1, 1 ), count );
			c = lerp( lerp( c00, c10, t.x ), lerp( c01, c11, t.x ), t.y );
		}
		else
		{
			c = ShadeLowResPixel( int2( floor( lpF ) ), count );
		}

		if ( crt )
			c = ApplyCrtOverlays( c, uv, outPx, lpF, blockPx, lowRes );

		c = lerp( original, c, saturate( g_flRetroBlend ) );

		return float4( c, 1.0 );
	}
}
