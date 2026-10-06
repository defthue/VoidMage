// VOID MAGE portal veil — the portal's vortex (void_portal.shader) as a fullscreen pass, for going through one.
//
//   mode 0, the pull:    the vortex grows out of the portal's place on screen. Inside the radius is the void: a
//                        hot horizon ring on the rim, spiral arms behind it, the dark heart in the middle. The
//                        radius runs far past the screen, so the arms sweep over the view and the heart is what
//                        is left: the player fell in.
//   mode 1, the arrival: the same ring turned inside out. A clear hole opens from the centre of the view and the
//                        void is what is OUTSIDE it, flung off the screen edges as the hole grows.
//
// On the clear side of the rim the frame is lensed toward the ring (the void bends what is next to it). One grab
// and one blit, inserted before the retro filter so the vortex is pixelated and graded with the world.
// PortalVeil.cs (Code/Rendering) drives every attribute. Globals are prefixed Veil: the per-view constant buffer
// already owns the generic names (see voidmage_retro.shader).

HEADER
{
	Description = "VOID MAGE portal veil — fullscreen void vortex for the portal transition";
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

	float g_flVeilMode < Attribute( "VeilMode" ); Default( 0.0 ); >;
	float g_flVeilCentreX < Attribute( "VeilCentreX" ); Default( 0.5 ); >;
	float g_flVeilCentreY < Attribute( "VeilCentreY" ); Default( 0.5 ); >;
	// Rim radius in screen heights.
	float g_flVeilRadius < Attribute( "VeilRadius" ); Default( 0.0 ); >;
	// 0..1 over the whole pass: the arrival fades its leftovers out with this.
	float g_flVeilStrength < Attribute( "VeilStrength" ); Default( 0.0 ); >;
	float g_flVeilPhase < Attribute( "VeilPhase" ); Default( 0.0 ); >;
	float g_flVeilLens < Attribute( "VeilLens" ); Default( 0.05 ); >;
	float g_flVeilTwist < Attribute( "VeilTwist" ); Default( 2.2 ); >;
	float3 g_vVeilColor < Attribute( "VeilColor" ); Default3( 0.55, 0.3, 0.95 ); >;

	float3 Veil_Fetch( float2 uv )
	{
		int2 p = clamp( int2( floor( uv * g_vViewportSize ) ), int2( 0, 0 ), int2( g_vViewportSize ) - 1 );
		return g_tColorBuffer.Load( int3( p, 0 ) ).rgb;
	}

	float Veil_Hash( float2 p )
	{
		float3 p3 = frac( float3( p.x, p.y, p.x ) * 0.1031 );
		p3 += dot( p3, p3.yzx + 33.33 );
		return frac( ( p3.x + p3.y ) * p3.z );
	}

	float Veil_Noise( float2 p )
	{
		float2 i = floor( p );
		float2 f = frac( p );
		float2 u = f * f * ( 3.0 - 2.0 * f );
		float a = Veil_Hash( i );
		float b = Veil_Hash( i + float2( 1.0, 0.0 ) );
		float c = Veil_Hash( i + float2( 0.0, 1.0 ) );
		float d = Veil_Hash( i + float2( 1.0, 1.0 ) );
		return lerp( lerp( a, b, u.x ), lerp( c, d, u.x ), u.y );
	}

	float Veil_Fbm( float2 p )
	{
		return Veil_Noise( p ) * 0.57 + Veil_Noise( p * 2.13 + 7.1 ) * 0.29 + Veil_Noise( p * 4.31 + 3.7 ) * 0.14;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = CalculateViewportUv( i.vPositionSs.xy );
		float strength = saturate( g_flVeilStrength );
		if ( strength <= 0.0 || g_flVeilRadius <= 0.0 )
			return float4( Veil_Fetch( uv ), 1.0 );

		float aspect = g_vViewportSize.x / max( g_vViewportSize.y, 1.0 );
		float2 toAspect = float2( aspect, 1.0 );
		float2 p = ( uv - float2( g_flVeilCentreX, g_flVeilCentreY ) ) * toAspect;
		float r = length( p );
		float2 dir = r > 1e-5 ? p / r : float2( 0.0, 1.0 );
		float a = atan2( p.y, p.x );
		float R = g_flVeilRadius;
		float rn = r / R;
		bool arriving = g_flVeilMode > 0.5;
		float t = g_flVeilPhase;

		// Depth into the void, 0 on the rim: toward the centre on the way in, toward the screen edges on the way out.
		float d = arriving ? ( rn - 1.0 ) / 1.2 : 1.0 - rn;

		// The rim is not a clean circle: it boils a little, in step with the arms.
		float lr = log( max( rn, 0.02 ) );
		float wobble = sin( 3.0 * a + t * 1.3 ) * 0.5 + sin( 7.0 * a - t * 2.3 ) * 0.3 + sin( 12.0 * a + t * 3.1 ) * 0.2;
		d += wobble * 0.025;

		// The clear side: the frame bends toward the rim, strongest right against it.
		float clearSide = saturate( -d );
		float pull = g_flVeilLens * strength * exp( -clearSide * 5.0 ) * ( arriving ? -1.0 : 1.0 );
		float3 scene = Veil_Fetch( uv + dir / toAspect * pull );
		// and takes a breath of the void's colour as the rim nears
		scene = lerp( scene, scene * ( 0.55 + g_vVeilColor ), exp( -clearSide * 7.0 ) * 0.6 * strength );

		// The void side. Same recipe as the portal sheet: twisted cartesian noise, integer-harmonic log spiral arms.
		float twistAng = g_flVeilTwist * lr + t * 0.35;
		float cs = cos( twistAng );
		float sn = sin( twistAng );
		float2 pn = p / R;
		float2 q = float2( pn.x * cs - pn.y * sn, pn.x * sn + pn.y * cs ) * 3.2;
		float f0 = frac( t * 0.16 );
		float f1 = frac( t * 0.16 + 0.5 );
		float n = Veil_Fbm( q * exp2( f0 ) + 11.3 ) * ( 1.0 - abs( 2.0 * f0 - 1.0 ) )
				+ Veil_Fbm( q * exp2( f1 ) + 47.9 ) * ( 1.0 - abs( 2.0 * f1 - 1.0 ) );
		float warp = ( n - 0.5 ) * 2.4;
		float arm1 = 0.5 + 0.5 * sin( 3.0 * a + g_flVeilTwist * 3.0 * lr + t * 1.3 + warp );
		float arm2 = 0.5 + 0.5 * sin( 5.0 * a + g_flVeilTwist * 4.5 * lr + t * 2.1 - warp * 1.4 );
		float arms = ( pow( arm1, 2.2 ) * 0.7 + pow( arm2, 3.3 ) * 0.3 ) * ( 0.55 + 0.9 * n );

		// Arms live in a band behind the rim; past it is the heart, and the heart is dark.
		float band = smoothstep( 0.0, 0.06, d ) * ( 1.0 - smoothstep( 0.30, 0.58, d ) );
		float v = arms * band;

		// The horizon: a thin hot ring riding the rim, hot spots chasing round it.
		float ring = exp( -( d * d ) / ( 0.022 * 0.022 ) );
		ring *= 0.75 + 0.25 * sin( 2.0 * a - t * 2.9 ) * sin( 3.0 * a + t * 1.7 );
		v += ring * 1.1;

		// Sparks falling along the spiral, one per angular cell (whole cells only: no atan2 seam).
		float cellCount = 40.0;
		float cellF = ( ( a + g_flVeilTwist * 0.6 * lr + t * 0.25 ) / 6.2831853 + 0.5 ) * cellCount;
		float cellId = fmod( fmod( floor( cellF ), cellCount ) + cellCount, cellCount );
		float h1 = Veil_Hash( float2( cellId, 1.7 ) );
		float h2 = Veil_Hash( float2( cellId, 9.3 ) );
		float fall = frac( h1 + t * ( 0.16 + 0.2 * h2 ) );
		float ds = fall * fall * 0.6;
		float len = 0.012 + 0.05 * fall;
		float across = 1.0 - abs( frac( cellF ) - 0.5 ) * 2.0;
		float streak = exp( -( ( d - ds ) * ( d - ds ) ) / ( len * len ) ) * pow( saturate( across ), 3.0 );
		streak *= smoothstep( 0.0, 0.15, fall ) * ( 1.0 - smoothstep( 0.8, 1.0, fall ) );
		v += streak * step( 0.0, d );

		float3 deep = g_vVeilColor * 0.012;
		float3 hot = lerp( g_vVeilColor, float3( 1.0, 0.95, 1.0 ), 0.75 ) * 1.8;
		float3 voidColor = lerp( deep, g_vVeilColor * 0.85, smoothstep( 0.0, 0.55, v ) );
		voidColor = lerp( voidColor, hot, smoothstep( 0.55, 1.25, v ) );

		// Opaque from the rim inward; the ring's glow spills a little onto the clear side.
		float cover = smoothstep( -0.012, 0.012, d );
		float3 color = lerp( scene, voidColor, cover * strength );
		color += hot * ring * ( 1.0 - cover ) * 0.6 * strength;

		return float4( color, 1.0 );
	}
}
