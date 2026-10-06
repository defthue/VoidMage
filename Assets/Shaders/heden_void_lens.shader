// HEDEN void lens — gravitational lensing for the void implosion: a fullscreen pass that
// pulls the already-rendered frame radially in around each live void orb, with a slight
// tangential swirl and chromatic fringing at the rim. In the dark chamber this is what
// makes the void undeniable: whatever light IS on screen visibly bends around it.
//
// Runs ONLY while orbs exist — VoidLensingEffect adds/removes the command list with the
// first/last orb, so the frame grab costs nothing the rest of the time. Blit skeleton
// after heden_toon_ao.
//
// Per-orb attribute (max 4): xy = screen centre in PIXELS, z = influence radius in
// pixels, w = strength (charge ramp, spiking at the implosion). Strength 0 = slot off.

HEADER
{
	Description = "HEDEN void lens — screen-space gravitational pull around void orbs";
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

	Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;

	float4 g_vVoidOrb0 < Attribute( "VoidOrb0" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
	float4 g_vVoidOrb1 < Attribute( "VoidOrb1" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
	float4 g_vVoidOrb2 < Attribute( "VoidOrb2" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
	float4 g_vVoidOrb3 < Attribute( "VoidOrb3" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;

	// Accumulates one orb's pull into the pixel-space offset; also darkens toward the
	// centre so the hole swallows what little the orb mesh doesn't cover.
	void AccumulateOrb( float4 orb, float2 ss, inout float2 offsetPx, inout float darken )
	{
		if ( orb.w <= 0.001 || orb.z <= 1.0 )
			return;

		float2 d = ss - orb.xy;
		float r = max( length( d ), 1.0 );

		if ( r >= orb.z )
			return;

		float fall = 1.0 - r / orb.z;

		// Pull grows toward the centre; capped so extreme spikes stay painterly, and the
		// sample can never be dragged further than the orb centre itself.
		float pullPx = orb.w * fall * fall * orb.z * 0.28;
		pullPx = min( pullPx, r * 0.85 );

		// A touch of frame-dragging: the pull direction rotates slightly around the orb.
		float2 dir = d / r;
		float spin = orb.w * fall * 0.55;
		float cs = cos( spin );
		float sn = sin( spin );
		float2 spun = float2( dir.x * cs - dir.y * sn, dir.x * sn + dir.y * cs );

		offsetPx += -spun * pullPx;
		darken += pow( fall, 6.0 ) * saturate( orb.w ) * 0.65;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = CalculateViewportUv( i.vPositionSs.xy );
		float2 ss = uv * g_vViewportSize;

		float2 offsetPx = float2( 0.0, 0.0 );
		float darken = 0.0;

		AccumulateOrb( g_vVoidOrb0, ss, offsetPx, darken );
		AccumulateOrb( g_vVoidOrb1, ss, offsetPx, darken );
		AccumulateOrb( g_vVoidOrb2, ss, offsetPx, darken );
		AccumulateOrb( g_vVoidOrb3, ss, offsetPx, darken );

		float2 offsetUv = offsetPx / g_vViewportSize;

		// Chromatic fringe: red bends a little more than blue — a violet-edged smear
		// exactly where the pull is strongest, free of any extra texture work.
		float rr = g_tColorBuffer.Sample( g_sBilinearMirror, uv + offsetUv * 1.12 ).r;
		float gg = g_tColorBuffer.Sample( g_sBilinearMirror, uv + offsetUv ).g;
		float bb = g_tColorBuffer.Sample( g_sBilinearMirror, uv + offsetUv * 0.88 ).b;

		float3 color = float3( rr, gg, bb ) * saturate( 1.0 - darken );

		return float4( color, 1.0 );
	}
}
