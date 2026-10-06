#ifndef HEDEN_WIND_HLSL
#define HEDEN_WIND_HLSL

#if ( S_WIND )

	float g_flToonWindAngle < Default( 0.0 ); Range( 0.0, 360.0 ); UiGroup( "Wind,30/10" ); >;
	float g_flToonWindSpeed < Default( 1.2 ); Range( 0.0, 8.0 ); UiGroup( "Wind,30/20" ); >;

	float g_flToonWindSway < Default( 2.0 ); Range( 0.0, 32.0 ); UiGroup( "Wind,30/30" ); >;
	float g_flToonWindFlutter < Default( 0.6 ); Range( 0.0, 16.0 ); UiGroup( "Wind,30/40" ); >;
	float g_flToonWindFlutterSpeed < Default( 4.0 ); Range( 1.0, 16.0 ); UiGroup( "Wind,30/50" ); >;

	// Spatial frequency of the flutter phase, as a multiplier on the base rate below.
	// 1.0 (default) = the original ~78-unit wavelength: parts of one tree crown are a full cycle
	// out of phase, so the crown BOILS instead of swaying. That is the worst possible motion for a
	// shadow caster — high-frequency, sub-texel and spatially incoherent is exactly the signal a
	// hard-thresholded shadow band cannot filter, and it shows up as a crawling shadow edge.
	// Lower values stretch the wavelength so a whole crown moves together and its cast silhouette
	// TRANSLATES rather than re-rasterising every frame. 0.1 = ~780 units, i.e. coherent per tree.
	// Left at 1.0 so every existing material renders bit-identically until it is dialled in.
	float g_flToonWindFlutterPhaseScale < Default( 1.0 ); Range( 0.05, 1.0 ); UiGroup( "Wind,30/55" ); >;

	float g_flToonWindHeightScale < Default( 128.0 ); Range( 1.0, 2048.0 ); UiGroup( "Wind,30/60" ); >;
	float g_flToonWindFalloff < Default( 2.0 ); Range( 1.0, 6.0 ); UiGroup( "Wind,30/70" ); >;

	// 0 = strength ramps with height only (old behaviour). 1 = strength ramps with DISTANCE from
	// the model origin, so a rounded crown sways at its outer shell — sides included — instead of
	// only at its top. Blend freely; rounded-canopy trees want ~0.7-0.9, trunk-y trees less.
	// HeightScale doubles as the normalising extent for the radial distance.
	float g_flToonWindRadial < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Wind,30/62" ); >;

	// Radial mode only: everything below this object-space height stays perfectly still, so roots
	// and low branches (far from origin but near the ground) never inherit radial sway.
	float g_flToonWindBaseStill < Default( 32.0 ); Range( 0.0, 512.0 ); UiGroup( "Wind,30/64" ); >;

	// Slow two-sine amplitude envelope on sway+flutter so wind gusts instead of ticking like a
	// metronome. 0 = constant amplitude (old behaviour); 1 = amplitude wanders between 0 and 2x.
	float g_flToonWindGust < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Wind,30/66" ); >;

	#if ( S_WIND_MASK )
		float g_flToonWindMaskChannel < Default( 2.0 ); Range( 0.0, 3.0 ); UiGroup( "Wind,30/80" ); >;
	#endif

	// Chop shudder, in world units of peak crown offset. A per-SceneObject attribute (like
	// HedenOutlineWarm), written as a decaying envelope by TreeFall on the client — so only the
	// tree being struck moves, and only while its node is held out of the instanced batch
	// (attributes don't reach batched draws). Rides the same strength falloff as the wind, so
	// the crown rattles hardest and the trunk base never moves.
	float g_flHedenTreeShake < Attribute( "HedenTreeShake" ); Default( 0.0 ); >;

	// Respawn grow-in: uniform object-space scale around the model origin, driven up to 1 by
	// TreeFall while the node is held out of the instanced batch. Applied to vPositionOs
	// BEFORE ProcessVertex in each shader's MainVs — correct under any rotation/scale, and
	// the outline shell grows in step for free. Default 1 = no-op for every batched draw.
	float g_flHedenTreeGrow < Attribute( "HedenTreeGrow" ); Default( 1.0 ); >;

	float Wind_Mask( float4 vertexColor )
	{
	#if ( S_WIND_MASK )
		float c = round( g_flToonWindMaskChannel );
		float4 sel = float4( c == 0.0, c == 1.0, c == 2.0, c == 3.0 );
		return saturate( dot( vertexColor, sel ) );
	#else
		return 1.0;
	#endif
	}

	float3 Wind_Offset( float3 positionOs, float3 worldPos, float mask )
	{
		float extent = max( g_flToonWindHeightScale, 0.0001 );

		float h = saturate( positionOs.z / extent );

		// Radial: distance from the model origin, gated to zero below BaseStill so low-but-far
		// geometry (roots, ground-level branches) stays planted.
		float r = saturate( length( positionOs ) / extent );
		r *= smoothstep( 0.0, max( g_flToonWindBaseStill, 0.0001 ), positionOs.z );

		float strength = pow( lerp( h, r, g_flToonWindRadial ), g_flToonWindFalloff ) * mask;

		if ( strength <= 0.0 )
			return float3( 0.0, 0.0, 0.0 );

		float t = g_flTime * g_flToonWindSpeed;

		float swayPhase = dot( worldPos.xy, float2( 0.0021, 0.0027 ) );
		float sway = sin( t + swayPhase );

		float flutterPhase = dot( worldPos.xyz, float3( 0.08, 0.09, 0.07 ) * g_flToonWindFlutterPhaseScale );
		float flutter = sin( t * g_flToonWindFlutterSpeed + flutterPhase );

		// Two slow incommensurate sines multiply into a wandering [-1,1] envelope; phases keyed
		// off swayPhase so neighbouring trees gust at different moments.
		float gust = sin( t * 0.31 + swayPhase * 2.3 ) * sin( t * 0.17 + 1.3 );
		float gustScale = 1.0 + g_flToonWindGust * gust;
		sway *= gustScale;
		flutter *= gustScale;

		float a = radians( g_flToonWindAngle );
		float3 dir = float3( cos( a ), sin( a ), 0.0 );

		float3 offset = dir * ( sway * g_flToonWindSway );
		offset += dir * ( flutter * g_flToonWindFlutter );
		offset.z += flutter * g_flToonWindFlutter * 0.4;

		// Chop shudder: fast, whole-tree-coherent rattle (phase keyed per tree via swayPhase,
		// not per vertex, so the tree moves as one thing). The attribute IS the amplitude.
		if ( g_flHedenTreeShake > 0.0 )
		{
			float st = g_flTime * 42.0;
			offset += float3(
				sin( st + swayPhase * 7.0 ),
				sin( st * 1.27 + swayPhase * 9.0 ),
				sin( st * 0.83 ) * 0.3 ) * g_flHedenTreeShake;
		}

		return offset * strength;
	}

#endif

#endif
