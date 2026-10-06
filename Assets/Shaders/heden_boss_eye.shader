//=========================================================================================================================
// HEDEN — boss eye: a self-lit sphere drawn as stepped concentric rings.
//
// The whole trick is the sphere's own normal. On a smooth-shaded true sphere the vertex
// normal IS the centre→surface direction, so 1 - dot(N, D) is the angular distance of
// each pixel from the point of the sphere that faces direction D. Banding that with toon
// steps gives rings that are always centred on D, from any view, with no mesh rotation:
//
//   F_EYE_STYLE 0 — "ring" eye: D is the camera direction. The rings are glued to the
//                   viewer (the Duskull eye): wherever you stand, it looks at you.
//   F_EYE_STYLE 1 — Abyssium eye: D is a world-space direction pushed from C#
//                   (BossEyeController → EyeTarget), so the pupil sits on the sphere facing
//                   the boss's TARGET, not the camera. A reptile eye: near-black sclera, a
//                   fibrous orange-red iris stepped into crisp bands, a dark limbus ring,
//                   and a needle slit with pointed tips (a vesica, not a pill).
//
// Unlit and opaque: the eye is a light source, not a lit surface. The core band is pushed
// past 1.0 (a float, not a colour picker — pickers clamp at 1) so it feeds bloom. Fog is
// applied like every other heden surface; darkness zones deliberately are NOT — a cave
// must never dim the boss's eyes.
//
// Mesh contract: UV sphere, Shade Smooth, uniform scale, skinned 100% to one bone.
// Faceted normals show up immediately as polygonal rings (EyeDebug 1 to check).
//
// Attributes (runtime, never in the vmat):
//   EyeTarget  float3  world unit direction eye→target (Abyssium); zero = look at the camera
//   EyeGlow    float   multiplies the core's overbright push (phase 2 up, death down)
//   EyePulse   float   0..1 spike — attack tell / hit; swells the rings, decays in code
//   EyeTint    float3  multiplies every band (phase-2 recolour)
//   EyeOpen    float   Abyssium slit width 0 (closed line) .. 1 (open) — stagger / death
//   EyeDilate  float   Abyssium slit dilation, 1 = authored; > 1 blows the pupil wide (lock-on)
//   EyeDebug   float   1 band coordinate, 2 slit distance, 3 pupil mask, 4 tangent frame
//=========================================================================================================================

HEADER
{
	Description = "HEDEN boss eye — unlit stepped-ring ball (camera-centred) or Abyssium slit eye (target-centred)";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_EYE_STYLE, 0..1 ( 0 = "Ring (camera-centred)", 1 = "Abyssium (target-centred slit eye)" ), "Eye" );
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
	#include "common/classes/Fog.hlsl"

	StaticCombo( S_EYE_STYLE, F_EYE_STYLE, Sys( ALL ) );

	// -- code-driven state ---------------------------------------------------------------

	float3 g_vEyeTarget < Attribute( "EyeTarget" ); Default3( 0.0, 0.0, 0.0 ); >;
	float g_flEyeGlow < Attribute( "EyeGlow" ); Default( 1.0 ); >;
	float g_flEyePulse < Attribute( "EyePulse" ); Default( 0.0 ); >;
	float3 g_vEyeTint < Attribute( "EyeTint" ); Default3( 1.0, 1.0, 1.0 ); >;
	float g_flEyeOpen < Attribute( "EyeOpen" ); Default( 1.0 ); >;

	// Slit dilation multiplier: 1 authored, > 1 the pupil blows wide (lock-on, rage), < 1 pins.
	float g_flEyeDilate < Attribute( "EyeDilate" ); Default( 1.0 ); >;
	float g_flEyeDebug < Attribute( "EyeDebug" ); Default( 0.0 ); >;

	// 1 while the wake-up scan runs: every Abyssium eye adds its OWN wander on top of the
	// shared look direction (per-material seed below), so the eyes search independently —
	// and all snap together the instant this drops to 0 (the lock).
	float g_flEyeScan < Attribute( "EyeScan" ); Default( 0.0 ); >;

	// -- material tunables ---------------------------------------------------------------

	// Four stepped colours, hot core outward. The core is a picker times a float so it can
	// exceed 1.0 and bloom (heden_beam's reasoning).
	float3 g_vColorCore < UiType( Color ); Default3( 1.0, 0.85, 0.92 ); UiGroup( "Bands,10/10" ); >;
	float3 g_vColorInner < UiType( Color ); Default3( 1.0, 0.30, 0.45 ); UiGroup( "Bands,10/11" ); >;
	float3 g_vColorOuter < UiType( Color ); Default3( 0.80, 0.06, 0.08 ); UiGroup( "Bands,10/12" ); >;
	float3 g_vColorEdge < UiType( Color ); Default3( 0.22, 0.02, 0.04 ); UiGroup( "Bands,10/13" ); >;
	float g_flCoreBrightness < Default( 2.5 ); Range( 1.0, 8.0 ); UiGroup( "Bands,10/14" ); >;

	// Where each band ends along r = 1 - dot(N, D): 0 at the point facing D, 1 at 90°.
	float g_flStep1 < Default( 0.10 ); Range( 0.0, 1.0 ); UiGroup( "Bands,10/20" ); >;
	float g_flStep2 < Default( 0.32 ); Range( 0.0, 1.0 ); UiGroup( "Bands,10/21" ); >;
	float g_flStep3 < Default( 0.62 ); Range( 0.0, 1.0 ); UiGroup( "Bands,10/22" ); >;
	float g_flStepSoftness < Default( 0.012 ); Range( 0.001, 0.2 ); UiGroup( "Bands,10/23" ); >;

	// Slow breathing of the ring radii; the code-driven pulse spikes on top of it.
	float g_flPulseSpeed < Default( 0.9 ); Range( 0.0, 5.0 ); UiGroup( "Pulse,10/30" ); >;
	float g_flPulseAmount < Default( 0.06 ); Range( 0.0, 0.5 ); UiGroup( "Pulse,10/31" ); >;

	// Abyssium eye only. Extents are in normal space (≈ sine of the angular size from D).
	// Iris radius: how far the iris reaches before the limbus ring and the sclera.
	float g_flIrisRadius < Default( 0.70 ); Range( 0.2, 1.0 ); UiGroup( "Abyssium,10/40" ); >;

	// Pulls the look direction toward the VIEWER: 0 = track the target fully, 1 = glued to
	// whoever is looking (the ring eye's behaviour). The sword/shield/trim eyes run high
	// values so their slits stay on the visible face of the ball instead of sliding around
	// the rim as the limb swings — you never see the back of those eyes.
	float g_flAimToView < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Abyssium,10/44" ); >;

	// Wake-scan wander, per material: phase seed keeps different eye groups searching
	// different directions, amount is the wander size (≈ tan of the angle; 0.35 ≈ 19°).
	float g_flScanSeed < Default( 0.0 ); Range( 0.0, 12.0 ); UiGroup( "Abyssium,10/45" ); >;
	float g_flScanAmount < Default( 0.35 ); Range( 0.0, 1.0 ); UiGroup( "Abyssium,10/46" ); >;
	float3 g_vColorSclera < UiType( Color ); Default3( 0.06, 0.005, 0.005 ); UiGroup( "Abyssium,10/41" ); >;
	float3 g_vColorLimbus < UiType( Color ); Default3( 0.12, 0.01, 0.01 ); UiGroup( "Abyssium,10/42" ); >;
	float g_flLimbusWidth < Default( 0.06 ); Range( 0.0, 0.3 ); UiGroup( "Abyssium,10/43" ); >;

	// The slit: half width at the centre, half height, and the taper exponent — 1 is a
	// lens with pointed tips, higher pulls the points into needles.
	float g_flSlitWidth < Default( 0.045 ); Range( 0.005, 0.3 ); UiGroup( "Abyssium,10/50" ); >;
	float g_flSlitHeight < Default( 0.46 ); Range( 0.05, 1.0 ); UiGroup( "Abyssium,10/51" ); >;
	float g_flSlitTaper < Default( 1.4 ); Range( 0.5, 4.0 ); UiGroup( "Abyssium,10/52" ); >;
	float3 g_vColorPupil < UiType( Color ); Default3( 0.01, 0.0, 0.0 ); UiGroup( "Abyssium,10/53" ); >;

	// Hot rim hugging the slit (normal-space width) and the bright ring at the pupil edge.
	float g_flHaloWidth < Default( 0.05 ); Range( 0.0, 0.4 ); UiGroup( "Abyssium,10/54" ); >;

	// Radial fibres: count around the pupil, how far they bend the band edges, drift speed.
	float g_flFibreScale < Default( 9.0 ); Range( 1.0, 30.0 ); UiGroup( "Abyssium,10/60" ); >;
	float g_flFibreStretch < Default( 1.5 ); Range( 0.0, 6.0 ); UiGroup( "Abyssium,10/61" ); >;
	float g_flFibreWarp < Default( 0.10 ); Range( 0.0, 0.4 ); UiGroup( "Abyssium,10/62" ); >;
	float g_flFibreSpeed < Default( 0.15 ); Range( 0.0, 2.0 ); UiGroup( "Abyssium,10/63" ); >;
	float g_flFlicker < Default( 0.08 ); Range( 0.0, 0.5 ); UiGroup( "Abyssium,10/64" ); >;

	// -- heden_water's noise, prefixed for this shader -----------------------------------

	float Eye_Hash( float2 p )
	{
		float3 p3 = frac( float3( p.x, p.y, p.x ) * 0.1031 );
		p3 += dot( p3, p3.yzx + 33.33 );
		return frac( ( p3.x + p3.y ) * p3.z );
	}

	float2 Eye_Grad( float2 p )
	{
		float ang = Eye_Hash( p ) * 6.2831853;
		return float2( cos( ang ), sin( ang ) );
	}

	float Eye_GradNoise( float2 p )
	{
		float2 i = floor( p );
		float2 f = frac( p );
		float2 u = f * f * f * ( f * ( f * 6.0 - 15.0 ) + 10.0 );

		float a = dot( Eye_Grad( i ), f );
		float b = dot( Eye_Grad( i + float2( 1.0, 0.0 ) ), f - float2( 1.0, 0.0 ) );
		float c = dot( Eye_Grad( i + float2( 0.0, 1.0 ) ), f - float2( 0.0, 1.0 ) );
		float d = dot( Eye_Grad( i + float2( 1.0, 1.0 ) ), f - float2( 1.0, 1.0 ) );

		return lerp( lerp( a, b, u.x ), lerp( c, d, u.x ), u.y );
	}

	// Four stepped bands along a 0..1 coordinate, hot core first.
	float3 Eye_Ramp( float r )
	{
		float s = g_flStepSoftness;
		float3 c = g_vColorCore * g_flCoreBrightness * g_flEyeGlow;
		c = lerp( c, g_vColorInner, smoothstep( g_flStep1 - s, g_flStep1 + s, r ) );
		c = lerp( c, g_vColorOuter, smoothstep( g_flStep2 - s, g_flStep2 + s, r ) );
		c = lerp( c, g_vColorEdge, smoothstep( g_flStep3 - s, g_flStep3 + s, r ) );
		return c;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		#if ( S_MODE_DEPTH )
			// Opaque ball — rasterized depth is all the shadow/prepass needs.
			return float4( 0.0, 0.0, 0.0, 1.0 );
		#endif

		float3 worldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 N = normalize( i.vNormalWs );
		float3 V = normalize( g_vCameraPositionWs.xyz - worldPos );

		// Breathing swells every ring together; dividing r by it grows the bands outward.
		float breath = 1.0 + g_flPulseAmount * sin( g_flTime * g_flPulseSpeed * 6.2831853 ) + g_flEyePulse * 0.35;

		float3 color;
		float r;

	#if ( S_EYE_STYLE == 0 )
		// ---- Ring eye: rings centred on whichever direction the camera looks from --------
		r = saturate( ( 1.0 - dot( N, V ) ) / breath );
		color = Eye_Ramp( r );
	#else
		// ---- Abyssium eye: iris + slit centred on the look direction, not the camera -----
		float3 D = g_vEyeTarget;
		float dl = length( D );
		D = ( dl > 0.5 ) ? D / dl : V;

		// Weapon/trim eyes lean toward the viewer so the slit never leaves the visible face.
		D = normalize( lerp( D, V, saturate( g_flAimToView ) ) );

		// The wake-up search: each material's seed swings its eyes on their own path, so
		// the whole body's eyes wander independently — TWITCHY, a furious panic (two
		// incommensurate frequencies per axis so the motion never settles into a loop) —
		// until the lock zeroes EyeScan and every one of them snaps onto the shared
		// direction at once.
		float scan = saturate( g_flEyeScan ) * g_flScanAmount;
		if ( scan > 0.001 )
		{
			float3 scanRight = cross( float3( 0.0, 0.0, 1.0 ), D );
			float srl = length( scanRight );
			scanRight = ( srl > 0.01 ) ? scanRight / srl : float3( 1.0, 0.0, 0.0 );
			float3 scanUp = cross( D, scanRight );

			float ph = g_flTime + g_flScanSeed;
			float ox = sin( ph * 5.7 + g_flScanSeed * 1.3 ) * 0.65 + sin( ph * 11.9 + g_flScanSeed * 2.1 ) * 0.35;
			float oy = ( sin( ph * 8.3 + g_flScanSeed * 2.7 + 0.8 ) * 0.65 + sin( ph * 14.7 + g_flScanSeed * 3.9 ) * 0.35 ) * 0.5;

			D = normalize( D + ( scanRight * ox + scanUp * oy ) * scan );
		}

		// Tangent frame around D. Source up is +Z; guarded for a straight-up/down D.
		float3 right = cross( float3( 0.0, 0.0, 1.0 ), D );
		float rl = length( right );
		right = ( rl > 0.01 ) ? right / rl : float3( 1.0, 0.0, 0.0 );
		float3 up = cross( D, right );

		float facing = dot( N, D );      // 1 dead on target, 0 at 90°, negative behind
		float x = dot( N, right );       // sideways lean of the normal
		float y = dot( N, up );
		float front = step( 0.0, facing );

		// Radial coordinate from the pupil centre: 0 at the centre, 1 at the 90° rim.
		float rad = length( float2( x, y ) );
		r = saturate( rad );

		// Iris coordinate: 0 at the pupil, 1 at the limbus. Breathing swells it slightly.
		float irisR = saturate( rad / ( max( g_flIrisRadius, 0.05 ) * breath ) );

		// Radial fibres: noise read on the unit circle (no seam) and stretched along the
		// radius, so it reads as strands running out from the pupil. They only bend the band
		// edges — the bands themselves stay crisp.
		float theta = atan2( y, x );
		float2 ring = float2( cos( theta ), sin( theta ) );
		float t = g_flTime * g_flFibreSpeed;
		float fibre = Eye_GradNoise( ring * g_flFibreScale + irisR * g_flFibreStretch + t )
			+ Eye_GradNoise( ring * g_flFibreScale * 2.1 + irisR * g_flFibreStretch * 1.7 + 5.3 - t * 0.7 ) * 0.5;

		float band = saturate( irisR + fibre * g_flFibreWarp );
		color = Eye_Ramp( band );

		// Limbus: the dark ring where the iris meets the sclera, then the sclera beyond it.
		float s = g_flStepSoftness;
		float limbus = smoothstep( 1.0 - g_flLimbusWidth - s, 1.0 - g_flLimbusWidth + s, irisR );
		float sclera = smoothstep( 1.0 - s, 1.0 + s, irisR );
		color = lerp( color, g_vColorLimbus, limbus );
		color = lerp( color, g_vColorSclera, sclera );

		// The slit: a vesica — half width shrinks toward the tips with a power taper, so the
		// ends come to points instead of rounding off. EyeOpen squeezes it toward a line,
		// EyeDilate blows it wide (the height grows with the square root so a dilated pupil
		// gets rounder, like a real iris opening).
		// The slit breathes WITH the rings, at half their amplitude — folded into the
		// dilation so the width takes the half-breath and the height its square root.
		float dilate = max( g_flEyeDilate, 0.05 ) * ( 1.0 + ( breath - 1.0 ) * 0.5 );
		float openScale = lerp( 0.08, 1.0, saturate( g_flEyeOpen ) );
		float slitHeight = max( g_flSlitHeight * sqrt( dilate ), 0.01 );
		float hy = abs( y ) / slitHeight;
		float taper = pow( saturate( 1.0 - hy * hy ), g_flSlitTaper );
		float halfWidth = g_flSlitWidth * openScale * dilate * taper;
		float pd = abs( x ) - halfWidth;                       // signed distance-ish to the slit edge
		float pupil = ( 1.0 - smoothstep( -s * 0.5, s * 0.5, pd ) ) * step( hy, 1.0 ) * front;

		// Hot rim hugging the slit: a second, slightly taller and wider vesica around the
		// first, so the glow tapers into the same points instead of cutting off square.
		float haloHeight = slitHeight + g_flHaloWidth;
		float hy2 = abs( y ) / haloHeight;
		float taper2 = pow( saturate( 1.0 - hy2 * hy2 ), g_flSlitTaper );
		float haloHalf = ( g_flSlitWidth * openScale * dilate + g_flHaloWidth ) * taper2;
		float haloT = saturate( ( abs( x ) - halfWidth ) / max( haloHalf - halfWidth, 0.001 ) );
		float halo = ( 1.0 - smoothstep( 0.0, 1.0, haloT ) ) * step( abs( x ), haloHalf ) * front;
		float flick = 1.0 + ( Eye_Hash( float2( floor( g_flTime * 14.0 ), 3.0 ) ) - 0.5 ) * g_flFlicker;
		color = lerp( color, g_vColorCore * g_flCoreBrightness * g_flEyeGlow * flick, halo * ( 1.0 - pupil ) );
		color = lerp( color, g_vColorPupil, pupil );
	#endif

		color *= g_vEyeTint;

		int dbg = (int)round( g_flEyeDebug );
		if ( dbg == 1 ) return float4( r.xxx, 1.0 );
	#if ( S_EYE_STYLE == 1 )
		if ( dbg == 2 ) return float4( irisR.xxx, 1.0 );
		if ( dbg == 3 ) return float4( pupil.xxx, 1.0 );
		if ( dbg == 4 ) return float4( x * 0.5 + 0.5, y * 0.5 + 0.5, front, 1.0 );
	#endif

		// Fog last, same ordering as Toon_Shade — atmosphere in front of the surface.
		color = Fog::Apply( worldPos, i.vPositionSs.xy, color );

		return float4( color, 1.0 );
	}
}
