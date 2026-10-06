#ifndef HEDEN_DISTANCE_FADE_HLSL
#define HEDEN_DISTANCE_FADE_HLSL

//
// Screen-space dither dissolve over the last stretch before a hard distance cull.
//
// The C# side culls instanced batches at InstancedPropBatch.MaxDrawDistance — a hard
// on/off per instance. This fades those instances out over the run-up to that distance
// so nothing snaps. Dither rather than alpha on purpose: the batches render opaque with
// WantsPrePass set, and the toon AO pass reads that prepass, so real translucency would
// put holes in AO. A dithered discard keeps the prepass honest — the pixels that survive
// are fully opaque, the ones that don't were never there.
//
// FadeEnd is per-SceneObject (set by InstancedPropBatch from its own MaxDrawDistance),
// not per-material, because every batch cell culls at its own distance. Anything that
// never sets it — characters, props with plain ModelRenderers — reads the default and
// never fades.
//
// Gated behind S_DISTANCE_FADE: a discard forces late depth-stencil, so materials that
// aren't drawn by a batch shouldn't pay for it.
//
#if ( S_DISTANCE_FADE )

	// Distance at which the object is fully gone. Default is past any plausible ZFar so
	// an unset attribute means "never fade" rather than "vanish immediately".
	float g_flHedenFadeEnd < Attribute( "HedenFadeEnd" ); Default( 1000000.0 ); >;

	// Width of the dissolve run-up ending at g_flHedenFadeEnd.
	float g_flHedenFadeBand < Attribute( "HedenFadeBand" ); Default( 800.0 ); >;

	//
	// Interleaved gradient noise (Jimenez). Stable per screen pixel, no texture, and its
	// neighbourhood spread is far more even than a 4x4 Bayer matrix — which matters here
	// because these are distant, small-on-screen objects where a tiled Bayer pattern
	// reads as visible cross-hatching rather than a dissolve.
	//
	// Screen-space, so the pattern does not swim across the surface as the object moves —
	// it stays pinned to the pixel grid, which is what makes a dither dissolve read as
	// "thinning out" instead of "crawling".
	//
	float DistanceFade_Noise( float2 screenPos )
	{
		return frac( 52.9829189 * frac( dot( screenPos, float2( 0.06711056, 0.00583715 ) ) ) );
	}

	// 1 = fully present, 0 = fully dissolved.
	float DistanceFade_Alpha( float3 worldPos )
	{
		float dist = distance( worldPos, g_vCameraPositionWs.xyz );
		float band = max( g_flHedenFadeBand, 1.0 );

		return saturate( ( g_flHedenFadeEnd - dist ) / band );
	}

	// Must be called before any S_MODE_DEPTH early-out: if the depth pass keeps pixels the
	// forward pass throws away, dissolved geometry still occludes and still casts shadow.
	void DistanceFade_Clip( float3 worldPos, float2 screenPos )
	{
		if ( DistanceFade_Alpha( worldPos ) < DistanceFade_Noise( screenPos ) )
			discard;
	}

#else

	void DistanceFade_Clip( float3 worldPos, float2 screenPos ) {}

#endif

#endif // HEDEN_DISTANCE_FADE_HLSL
