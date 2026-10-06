// HEDEN void floor rift — the void implosion's ground presence, both phases in one quad:
//
//   CHARGE  a dark pool: black-violet fill spiralling INWARD (draining down into the
//           singularity), edged by a thin violet ring at the exact gameplay radius.
//   SHOCK   the implosion's blast: a hot white-violet thin ring racing outward from the
//           centre with a fainter echo ring trailing it — crisp drawn circles, no
//           particles involved.
//
// Skeleton, world-space polar frame and depth feather are heden_heal_circle's verbatim;
// the noise library is the same construction renamed.
//
// Driven per-frame from VoidImplosionVfx through attributes:
//   g_flVoidCharge  0..1+  build-up (fill presence, swirl speed; the inhale pushes >1)
//   g_flVoidShock   0 off, else 0..1 — the shock ring's progress to 1.35x radius
//   g_flVoidFade    1..0   global alpha envelope
//   g_vVoidColor    rgb    spell colour
//   g_vCircleCenter / g_flCircleRadius — world-unit placement, as the heal circle

HEADER
{
	Description = "HEDEN void floor rift — inward-draining dark pool + implosion shock rings";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		return FinalizeVertex( o );
	}
}

PS
{
	#define CUSTOM_MATERIAL_INPUTS
	#include "common/pixel.hlsl"

	// Translucent ground quad, never depth-written — heden_heal_circle's block.
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
	RenderState( DepthWriteEnable, false );
	RenderState( CullMode, NONE );

	float g_flVoidCharge < Attribute( "g_flVoidCharge" ); Default( 0.0 ); >;
	float g_flVoidShock < Attribute( "g_flVoidShock" ); Default( 0.0 ); >;
	float g_flVoidFade < Attribute( "g_flVoidFade" ); Default( 1.0 ); >;
	float3 g_vVoidColor < Attribute( "g_vVoidColor" ); Default3( 0.5, 0.12, 0.9 ); >;
	float3 g_vCircleCenter < Attribute( "g_vCircleCenter" ); >;
	float g_flCircleRadius < Attribute( "g_flCircleRadius" ); Default( 140.0 ); >;

	// World units over which the quad fades into uneven terrain (heden_beam's feather).
	float g_flDepthFeather < Default( 12.0 ); Range( 0.0, 64.0 ); UiGroup( "Blend,10/40" ); >;

	// -- heden_water's noise, prefixed for this shader -----------------------------------

	float Void_Hash( float2 p )
	{
		float3 p3 = frac( float3( p.x, p.y, p.x ) * 0.1031 );
		p3 += dot( p3, p3.yzx + 33.33 );
		return frac( ( p3.x + p3.y ) * p3.z );
	}

	float2 Void_Grad( float2 p )
	{
		float ang = Void_Hash( p ) * 6.2831853;
		return float2( cos( ang ), sin( ang ) );
	}

	float Void_GradNoise( float2 p )
	{
		float2 i = floor( p );
		float2 f = frac( p );
		float2 u = f * f * f * ( f * ( f * 6.0 - 15.0 ) + 10.0 );

		float a = dot( Void_Grad( i ), f );
		float b = dot( Void_Grad( i + float2( 1.0, 0.0 ) ), f - float2( 1.0, 0.0 ) );
		float c = dot( Void_Grad( i + float2( 0.0, 1.0 ) ), f - float2( 0.0, 1.0 ) );
		float d = dot( Void_Grad( i + float2( 1.0, 1.0 ) ), f - float2( 1.0, 1.0 ) );

		return lerp( lerp( a, b, u.x ), lerp( c, d, u.x ), u.y );
	}

	float Void_PatchNoise( float2 p, float t )
	{
		const float2x2 rot = float2x2( 0.80, -0.60, 0.60, 0.80 );

		float2 warp = float2(
			Void_GradNoise( p * 0.5 + float2( 7.3, 1.7 ) + t * 0.06 ),
			Void_GradNoise( p * 0.5 + float2( 3.1, 9.2 ) - t * 0.05 ) );
		p += warp * 0.7;

		float n = Void_GradNoise( p + float2( t * 0.35, t * 0.12 ) ) * 0.55;
		p = mul( rot, p ) * 2.1;
		n += Void_GradNoise( p + float2( -t * 0.22, t * 0.07 ) ) * 0.30;
		p = mul( rot, p ) * 1.9;
		n += Void_GradNoise( p + float2( t * 0.13, -t * 0.16 ) ) * 0.15;

		return saturate( n * 0.75 + 0.5 );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 worldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;

		// World-space polar frame: r = 1 exactly at the gameplay radius, everywhere.
		float2 rel = worldPos.xy - g_vCircleCenter.xy;
		float r = length( rel ) / max( g_flCircleRadius, 1.0 );
		float theta = atan2( rel.y, rel.x );
		float2 ringPoint = float2( cos( theta ), sin( theta ) );
		float t = g_flTime;

		float charge = g_flVoidCharge;

		float3 color = float3( 0.0, 0.0, 0.0 );
		float alpha = 0.0;

		// -- charge: the draining pool ---------------------------------------------------
		if ( charge > 0.001 )
		{
			// The spiral runs INWARD: the sample angle twists tighter with radius and the
			// radial term drags the pattern toward the centre over time — everything in
			// the pool visibly falls down the drain, faster as the charge builds.
			float drain = t * ( 0.5 + 0.9 * charge );
			float swirlAngle = theta - drain * 1.6 + r * 3.2;
			float2 sp = float2( cos( swirlAngle ), sin( swirlAngle ) ) * ( r * 2.4 + drain * 0.35 );
			float swirl = Void_PatchNoise( sp, t );
			swirl = smoothstep( 0.30, 0.85, swirl );

			// Deepest at the centre, gone at the edge ring.
			float pool = ( 1.0 - smoothstep( 0.15, 1.0, r ) ) * ( 0.5 + 0.5 * charge );

			// The pool DARKENS the ground (near-black violet) with brighter violet strands
			// swirling through it — a hole filling with un-light.
			float3 poolColor = lerp( g_vVoidColor * 0.02, g_vVoidColor * 0.55, swirl * 0.6 );
			float poolAlpha = pool * ( 0.55 + 0.25 * swirl );

			// The danger edge: a violet ring at EXACTLY the gameplay radius — the blast
			// range, readable in the dark from birth, breathing so it reads as live.
			float wob = Void_GradNoise( ringPoint * 3.0 + float2( t * 0.4, 0.0 ) ) * 0.012;
			float breathe = 0.85 + 0.15 * sin( t * 3.2 );
			float edge = 1.0 - smoothstep( 0.0, 0.055, abs( r - ( 1.0 + wob ) ) );
			float3 edgeColor = g_vVoidColor * ( 2.4 + 2.2 * charge ) * breathe;

			// The TIMER ring: a hotter ring expanding from the centre with the charge,
			// touching the edge EXACTLY at detonation — where it is, the blast will be;
			// when it arrives, boom. Whitens as it closes in.
			float timer = 1.0 - smoothstep( 0.0, 0.03, abs( r - saturate( charge ) ) );
			float closing = smoothstep( 0.6, 1.0, charge );
			float3 timerColor = lerp( g_vVoidColor * 1.8, float3( 1.0, 0.9, 1.0 ) * 2.2, closing );

			color = poolColor + edgeColor * edge + timerColor * timer;
			alpha = poolAlpha + edge * ( 0.55 + 0.4 * charge ) + timer * ( 0.5 + 0.4 * closing );
		}

		// -- shock: the implosion's rings ------------------------------------------------
		if ( g_flVoidShock > 0.001 )
		{
			float progress = saturate( g_flVoidShock );

			// The main front races to 1.35x the radius, thinning and cooling as it goes.
			float front = progress * 1.35;
			float width = lerp( 0.020, 0.10, progress );
			float main = 1.0 - smoothstep( 0.0, width, abs( r - front ) );

			// The echo trails at 75% of the front, half as wide, half as bright.
			float echo = 1.0 - smoothstep( 0.0, width * 0.5, abs( r - front * 0.75 ) );

			float heat = 1.0 - progress;
			float3 shockColor = lerp( g_vVoidColor, float3( 1.0, 0.95, 1.0 ), heat * heat );

			color += shockColor * ( main * ( 1.2 + 4.0 * heat ) + echo * ( 0.5 + 1.5 * heat ) );
			alpha += ( main + echo * 0.55 ) * ( 1.0 - progress * 0.55 );
		}

		alpha = saturate( alpha ) * saturate( g_flVoidFade );

		// Soft landing on uneven terrain — heden_beam's feather via scene depth.
		if ( g_flDepthFeather > 0 )
		{
			float3 scenePos = Depth::GetWorldPosition( i.vPositionSs.xy );
			alpha *= clamp( distance( scenePos, worldPos ) / g_flDepthFeather, 0.0, 1.0 );
		}

		clip( alpha - 0.001 );

		return float4( color, saturate( alpha ) );
	}
}
