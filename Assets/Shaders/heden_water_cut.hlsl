//=========================================================================================================================
// HEDEN — water exclusion zones ("negative water")
//
// Per-pixel holes in water, pushed globally by WaterExclusionSystem (Code/Client/WaterExclusionZone.cs).
// Included by heden_water.shader only: a pixel of ANY water surface whose world position falls
// inside one of the boxes is clipped before anything else runs, so a cave or dungeon room below
// sea level stops showing the ocean plane through its walls (and the underwater backface wall
// when the camera is below the plane).
//
// Boxes are ORIENTED and the test is deliberately the cheapest one possible: the C# side sends
// each box as three rows of its world -> NORMALIZED-local affine transform (each row already
// divided by that axis' half extent), so per slot the shader does three dot4s and one abs/compare
// — no cross product, no divides, no branch. Inside = all(|lp| < 1).
//
// Eight fixed slots (shaders take no lists; the system streams the 8 zones nearest the camera,
// the scene may hold any number). Per slot, three vectors:
//   R0 = ( axisX / halfX,  -dot( center, axisX ) / halfX )
//   R1 = ( axisY / halfY,  -dot( center, axisY ) / halfY )
//   R2 = ( axisZ / halfZ,  -dot( center, axisZ ) / halfZ )
// EMPTY SLOT SENTINEL: R0 = (0,0,0,2), R1 = R2 = 0 — that makes lp.x a constant 2, i.e. always
// outside, without a per-slot "is this slot live" branch. The Default4 values below ARE that
// sentinel, so a scene with no system pushing renders bit-identically to before this existed.
//=========================================================================================================================

#ifndef HEDEN_WATER_CUT_HLSL
#define HEDEN_WATER_CUT_HLSL

#define HEDEN_WATER_CUT_SLOTS 8

float4 g_vWaterCutR0_0 < Attribute( "hedenWaterCutR0_0" ); Default4( 0.0, 0.0, 0.0, 2.0 ); >;
float4 g_vWaterCutR1_0 < Attribute( "hedenWaterCutR1_0" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vWaterCutR2_0 < Attribute( "hedenWaterCutR2_0" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vWaterCutR0_1 < Attribute( "hedenWaterCutR0_1" ); Default4( 0.0, 0.0, 0.0, 2.0 ); >;
float4 g_vWaterCutR1_1 < Attribute( "hedenWaterCutR1_1" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vWaterCutR2_1 < Attribute( "hedenWaterCutR2_1" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vWaterCutR0_2 < Attribute( "hedenWaterCutR0_2" ); Default4( 0.0, 0.0, 0.0, 2.0 ); >;
float4 g_vWaterCutR1_2 < Attribute( "hedenWaterCutR1_2" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vWaterCutR2_2 < Attribute( "hedenWaterCutR2_2" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vWaterCutR0_3 < Attribute( "hedenWaterCutR0_3" ); Default4( 0.0, 0.0, 0.0, 2.0 ); >;
float4 g_vWaterCutR1_3 < Attribute( "hedenWaterCutR1_3" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vWaterCutR2_3 < Attribute( "hedenWaterCutR2_3" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vWaterCutR0_4 < Attribute( "hedenWaterCutR0_4" ); Default4( 0.0, 0.0, 0.0, 2.0 ); >;
float4 g_vWaterCutR1_4 < Attribute( "hedenWaterCutR1_4" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vWaterCutR2_4 < Attribute( "hedenWaterCutR2_4" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vWaterCutR0_5 < Attribute( "hedenWaterCutR0_5" ); Default4( 0.0, 0.0, 0.0, 2.0 ); >;
float4 g_vWaterCutR1_5 < Attribute( "hedenWaterCutR1_5" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vWaterCutR2_5 < Attribute( "hedenWaterCutR2_5" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vWaterCutR0_6 < Attribute( "hedenWaterCutR0_6" ); Default4( 0.0, 0.0, 0.0, 2.0 ); >;
float4 g_vWaterCutR1_6 < Attribute( "hedenWaterCutR1_6" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vWaterCutR2_6 < Attribute( "hedenWaterCutR2_6" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vWaterCutR0_7 < Attribute( "hedenWaterCutR0_7" ); Default4( 0.0, 0.0, 0.0, 2.0 ); >;
float4 g_vWaterCutR1_7 < Attribute( "hedenWaterCutR1_7" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vWaterCutR2_7 < Attribute( "hedenWaterCutR2_7" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;

// Is the (homogeneous) world position inside one box? The rows carry the box into a unit cube.
bool Water_CutOne( float4 p4, float4 r0, float4 r1, float4 r2 )
{
	float3 lp = float3( dot( r0, p4 ), dot( r1, p4 ), dot( r2, p4 ) );
	return all( abs( lp ) < 1.0 );
}

// True if any streamed exclusion zone covers this world position — the caller clips the pixel.
// Eight slots unrolled: ~10 ALU per slot, paid on water pixels only.
bool Water_IsCut( float3 worldPos )
{
	float4 p4 = float4( worldPos, 1.0 );
	bool cut = Water_CutOne( p4, g_vWaterCutR0_0, g_vWaterCutR1_0, g_vWaterCutR2_0 );
	cut = cut || Water_CutOne( p4, g_vWaterCutR0_1, g_vWaterCutR1_1, g_vWaterCutR2_1 );
	cut = cut || Water_CutOne( p4, g_vWaterCutR0_2, g_vWaterCutR1_2, g_vWaterCutR2_2 );
	cut = cut || Water_CutOne( p4, g_vWaterCutR0_3, g_vWaterCutR1_3, g_vWaterCutR2_3 );
	cut = cut || Water_CutOne( p4, g_vWaterCutR0_4, g_vWaterCutR1_4, g_vWaterCutR2_4 );
	cut = cut || Water_CutOne( p4, g_vWaterCutR0_5, g_vWaterCutR1_5, g_vWaterCutR2_5 );
	cut = cut || Water_CutOne( p4, g_vWaterCutR0_6, g_vWaterCutR1_6, g_vWaterCutR2_6 );
	cut = cut || Water_CutOne( p4, g_vWaterCutR0_7, g_vWaterCutR1_7, g_vWaterCutR2_7 );
	return cut;
}

#endif // HEDEN_WATER_CUT_HLSL
