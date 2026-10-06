//=========================================================================================================================
// HEDEN — darkness zones (shared)
//
// Per-pixel cave darkness, pushed globally by DarknessZoneSystem (Code/Client/DarknessZone.cs).
// Included by heden_toon_core.hlsl (surfaces) and heden_outline.shader (ink lines), so a
// marked cave darkens everything consistently.
//
// Boxes are ORIENTED: each pixel is transformed into the zone's local frame (two axes
// streamed, the third derived), so rotating the zone's GameObject rotates the darkness
// exactly as the gizmo shows. Four fixed slots (shaders take no lists; the system sends
// the 4 zones nearest the camera, the scene can hold any number — every slot is paid per
// pixel in two passes, so the count stays as small as the content needs). Per slot, five
// vectors:
//   Center  = world center.xyz, w = darkness strength (0 = slot empty — the default,
//             so scenes without zones render bit-identically)
//   Half    = local half-size.xyz, w = -X face softness
//   AxisX   = the zone's world-space X axis (forward), w = -Y face softness
//   AxisY   = the zone's world-space Y axis (left),    w = -Z face softness
//   SoftPos = +X / +Y / +Z face softness (xyz), w = SunSpare (0..1)
// Per-FACE softness in world units: a cave-mouth face blends over 100+ units while
// faces buried in rock cut hard (softness ~1).
//
// SunSpare: how much of the pixel's DIRECT sunlight the darkness leaves alone. 0 (the
// default) means the zone blocks the sun itself — a cave is roofed by definition, and
// the sun's shadow map is an unreliable proxy for that: past a cascade edge it reports
// "lit", which left straight-edged bands of daylight deep inside the dungeon. 1 is the
// old behaviour (sun-lit pixels spared entirely); only for a zone that wants real
// shadow-mapped sun shafts inside the box and can live with the cascade edges.
//=========================================================================================================================

#ifndef HEDEN_DARKNESS_ZONES_HLSL
#define HEDEN_DARKNESS_ZONES_HLSL

float4 g_vDarkZoneCenter0 < Attribute( "hedenDarkZoneCenter0" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vDarkZoneHalf0 < Attribute( "hedenDarkZoneHalf0" ); Default4( 0.0, 0.0, 0.0, 1.0 ); >;
float4 g_vDarkZoneAxisX0 < Attribute( "hedenDarkZoneAxisX0" ); Default4( 1.0, 0.0, 0.0, 1.0 ); >;
float4 g_vDarkZoneAxisY0 < Attribute( "hedenDarkZoneAxisY0" ); Default4( 0.0, 1.0, 0.0, 1.0 ); >;
float4 g_vDarkZoneSoftPos0 < Attribute( "hedenDarkZoneSoftPos0" ); Default4( 1.0, 1.0, 1.0, 0.0 ); >;
float4 g_vDarkZoneCenter1 < Attribute( "hedenDarkZoneCenter1" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vDarkZoneHalf1 < Attribute( "hedenDarkZoneHalf1" ); Default4( 0.0, 0.0, 0.0, 1.0 ); >;
float4 g_vDarkZoneAxisX1 < Attribute( "hedenDarkZoneAxisX1" ); Default4( 1.0, 0.0, 0.0, 1.0 ); >;
float4 g_vDarkZoneAxisY1 < Attribute( "hedenDarkZoneAxisY1" ); Default4( 0.0, 1.0, 0.0, 1.0 ); >;
float4 g_vDarkZoneSoftPos1 < Attribute( "hedenDarkZoneSoftPos1" ); Default4( 1.0, 1.0, 1.0, 0.0 ); >;
float4 g_vDarkZoneCenter2 < Attribute( "hedenDarkZoneCenter2" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vDarkZoneHalf2 < Attribute( "hedenDarkZoneHalf2" ); Default4( 0.0, 0.0, 0.0, 1.0 ); >;
float4 g_vDarkZoneAxisX2 < Attribute( "hedenDarkZoneAxisX2" ); Default4( 1.0, 0.0, 0.0, 1.0 ); >;
float4 g_vDarkZoneAxisY2 < Attribute( "hedenDarkZoneAxisY2" ); Default4( 0.0, 1.0, 0.0, 1.0 ); >;
float4 g_vDarkZoneSoftPos2 < Attribute( "hedenDarkZoneSoftPos2" ); Default4( 1.0, 1.0, 1.0, 0.0 ); >;
float4 g_vDarkZoneCenter3 < Attribute( "hedenDarkZoneCenter3" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vDarkZoneHalf3 < Attribute( "hedenDarkZoneHalf3" ); Default4( 0.0, 0.0, 0.0, 1.0 ); >;
float4 g_vDarkZoneAxisX3 < Attribute( "hedenDarkZoneAxisX3" ); Default4( 1.0, 0.0, 0.0, 1.0 ); >;
float4 g_vDarkZoneAxisY3 < Attribute( "hedenDarkZoneAxisY3" ); Default4( 0.0, 1.0, 0.0, 1.0 ); >;
float4 g_vDarkZoneSoftPos3 < Attribute( "hedenDarkZoneSoftPos3" ); Default4( 1.0, 1.0, 1.0, 0.0 ); >;

// Per-SceneObject membership flag, set by DarknessGroup (Code/Client/DarknessGroup.cs) on
// every renderer under a marked root (the dungeon). 0 — the default, so untouched objects
// render bit-identically — means "normal object": darkness applies by the 3D volume only.
// > 0 means "this object IS the dark interior": it additionally takes the zones' 2D
// FOOTPRINT darkness (X/Y fades intact, the Z test ignored), scaled by this value. Why:
// the mine quarry digs down INTO the dungeon's airspace, so no zone box top can cover the
// dungeon ceiling without per-pixel darkening the quarry cliffs above — membership must
// come from the object hierarchy, not from a volume. Same per-draw mechanism as
// HedenLandmarkFarZ (SceneObject.Attributes; scene-global is the fallback default).
float g_flHedenObjectDarkness < Attribute( "HedenObjectDarkness" ); Default( 0.0 ); >;

// One zone's darkness at a world position: the pixel is dropped into the zone's local
// frame (axes streamed from the GameObject's rotation, Z derived by cross — s&box is
// right-handed, Z-up), then it is the same box test as ever: full strength inside,
// fading to zero outside over each FACE's own softness. The distance on each axis is
// normalized by that side's softness before combining, so a cave-mouth face can blend
// over 100+ units while a face buried in rock (softness ~1) cuts hard.
float Toon_OneZoneMasked( float3 p, float4 center, float4 halfSize, float4 axisX, float4 axisY, float4 softPos4, float zMask )
{
	if ( center.w <= 0.0 )
		return 0.0;

	float3 rel = p - center.xyz;
	float3 axisZ = cross( axisX.xyz, axisY.xyz );
	float3 lp = float3( dot( rel, axisX.xyz ), dot( rel, axisY.xyz ), dot( rel, axisZ ) );

	float3 softNeg = max( float3( halfSize.w, axisX.w, axisY.w ), 1.0 );
	float3 softPos = max( softPos4.xyz, 1.0 );

	// Per axis only one side can be positive, so the normalized halves add cleanly.
	float3 dNorm = max( -halfSize.xyz - lp, 0.0 ) / softNeg
		+ max( lp - halfSize.xyz, 0.0 ) / softPos;

	// zMask 0 = footprint mode: the Z faces don't exist, only the X/Y fades matter.
	dNorm.z *= zMask;

	return center.w * saturate( 1.0 - length( dNorm ) );
}

float Toon_OneZone( float3 p, float4 center, float4 halfSize, float4 axisX, float4 axisY, float4 softPos4 )
{
	return Toon_OneZoneMasked( p, center, halfSize, axisX, axisY, softPos4, 1.0 );
}

// The zones' darkness as a 2D FOOTPRINT: the same X/Y fades (cave mouths keep their
// per-pixel blend) but the Z test dropped, as if every pixel sat at box-center height.
// Only meaningful for objects flagged with HedenObjectDarkness — the dark interior is
// dark at ANY height (ceiling, balconies, exit stairs), while unflagged surface content
// above the box keeps the normal Z-bounded volume behaviour.
float Toon_ZoneFootprint( float3 p )
{
	float dark = Toon_OneZoneMasked( p, g_vDarkZoneCenter0, g_vDarkZoneHalf0, g_vDarkZoneAxisX0, g_vDarkZoneAxisY0, g_vDarkZoneSoftPos0, 0.0 );
	dark = max( dark, Toon_OneZoneMasked( p, g_vDarkZoneCenter1, g_vDarkZoneHalf1, g_vDarkZoneAxisX1, g_vDarkZoneAxisY1, g_vDarkZoneSoftPos1, 0.0 ) );
	dark = max( dark, Toon_OneZoneMasked( p, g_vDarkZoneCenter2, g_vDarkZoneHalf2, g_vDarkZoneAxisX2, g_vDarkZoneAxisY2, g_vDarkZoneSoftPos2, 0.0 ) );
	dark = max( dark, Toon_OneZoneMasked( p, g_vDarkZoneCenter3, g_vDarkZoneHalf3, g_vDarkZoneAxisX3, g_vDarkZoneAxisY3, g_vDarkZoneSoftPos3, 0.0 ) );
	return dark;
}

// The darkest zone covering this pixel (max, not sum — overlapping caves are one cave),
// and that zone's SunSpare. Four fixed slots; a handful of dots per empty slot, cheaper
// than one torch.
void Toon_ZoneDarkness2( float3 p, out float dark, out float sunSpare )
{
	dark = Toon_OneZone( p, g_vDarkZoneCenter0, g_vDarkZoneHalf0, g_vDarkZoneAxisX0, g_vDarkZoneAxisY0, g_vDarkZoneSoftPos0 );
	sunSpare = g_vDarkZoneSoftPos0.w;

	float d1 = Toon_OneZone( p, g_vDarkZoneCenter1, g_vDarkZoneHalf1, g_vDarkZoneAxisX1, g_vDarkZoneAxisY1, g_vDarkZoneSoftPos1 );
	if ( d1 > dark ) { dark = d1; sunSpare = g_vDarkZoneSoftPos1.w; }

	float d2 = Toon_OneZone( p, g_vDarkZoneCenter2, g_vDarkZoneHalf2, g_vDarkZoneAxisX2, g_vDarkZoneAxisY2, g_vDarkZoneSoftPos2 );
	if ( d2 > dark ) { dark = d2; sunSpare = g_vDarkZoneSoftPos2.w; }

	float d3 = Toon_OneZone( p, g_vDarkZoneCenter3, g_vDarkZoneHalf3, g_vDarkZoneAxisX3, g_vDarkZoneAxisY3, g_vDarkZoneSoftPos3 );
	if ( d3 > dark ) { dark = d3; sunSpare = g_vDarkZoneSoftPos3.w; }

	sunSpare = saturate( sunSpare );
}

// Darkness alone — the outline shader only needs this.
float Toon_ZoneDarkness( float3 p )
{
	float dark, sunSpare;
	Toon_ZoneDarkness2( p, dark, sunSpare );
	return dark;
}

#endif // HEDEN_DARKNESS_ZONES_HLSL
