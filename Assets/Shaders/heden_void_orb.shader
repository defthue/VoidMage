// HEDEN void orb — the body of the void implosion: an opaque near-black sphere whose
// silhouette writhes (vertex roil) with an animated violet accretion rim. A black hole is
// OPAQUE by nature, so no translucency bucket games — the "see-through" reading comes from
// the screen-space lens pass (heden_void_lens) bending the scene around it.
//
// Driven per-instance through SceneObject attributes by VoidImplosionVfx:
//   VoidCharge   0..1  build-up over the zone's delay (rim energy, roil amplitude)
//   VoidCollapse 0..1  the implosion crush (rim flares white-hot as it dies)
//   VoidColor    rgb   the spell's colour (BossPlacedSpell.Color)

FEATURES
{
    #include "common/features.hlsl"
}

MODES
{
    Forward();
    Depth();
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

    float g_flVoidCharge < Attribute( "VoidCharge" ); Default( 0.0 ); >;

    PixelInput MainVs( VertexInput i )
    {
        // Surface roil: the sphere's shell breathes with layered sines — enough to make
        // the silhouette visibly alive, never enough to read as a different shape. The
        // dev sphere is origin-centred, so the outward direction IS the position.
        float3 p = i.vPositionOs.xyz;
        float t = g_flTime;

        float roil = sin( p.x * 0.35 + t * 3.1 ) * sin( p.y * 0.29 - t * 2.3 ) * sin( p.z * 0.31 + t * 2.7 )
                   + 0.5 * sin( p.y * 0.61 + t * 5.3 ) * sin( p.z * 0.57 - t * 4.1 );

        // Barely-there: v1's big wobble read as a water ball. The silhouette should only
        // just quiver — a hole under strain, not a liquid.
        float amplitude = 0.012 + 0.014 * g_flVoidCharge;
        i.vPositionOs.xyz = p * ( 1.0 + roil * amplitude );

        PixelInput o = ProcessVertex( i );
        return FinalizeVertex( o );
    }
}

PS
{
    #define CUSTOM_MATERIAL_INPUTS
    #include "common/pixel.hlsl"

    float g_flVoidCharge < Attribute( "VoidCharge" ); Default( 0.0 ); >;
    float g_flVoidCollapse < Attribute( "VoidCollapse" ); Default( 0.0 ); >;
    float3 g_vVoidColor < Attribute( "VoidColor" ); Default3( 0.5, 0.12, 0.9 ); >;

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        float3 worldPos = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
        float3 n = normalize( i.vNormalWs );
        float3 view = normalize( g_vCameraPositionWs - worldPos );
        float t = g_flTime;

        // Black-hole language, not water: the body is PITCH dark and all the light lives
        // in a razor-thin EVENT-HORIZON ring hugging the silhouette. v1's broad soft
        // fresnel was the "purple water ball".
        float fresnel = 1.0 - saturate( dot( n, view ) );

        // The thin band: rises fast near the silhouette, cut off again right at the very
        // edge so it stays a crisp line instead of a glow.
        float ring = smoothstep( 0.70, 0.84, fresnel ) * ( 1.0 - smoothstep( 0.90, 0.97, fresnel ) );

        // Brightness swirls AROUND the ring — two beating frequencies (the boss-eye
        // trick) so hot spots chase each other along the horizon without ever looping.
        float angle = atan2( n.y, n.x );
        float swirl = sin( angle * 3.0 + n.z * 4.0 - t * ( 2.6 + 2.4 * g_flVoidCharge ) )
                    * sin( angle * 5.0 - n.z * 2.0 + t * 3.7 );
        float bands = 0.6 + 0.4 * swirl;

        // The collapse whitens and floods the ring — the last light falling in.
        float3 ringColor = lerp( g_vVoidColor, float3( 1.0, 0.92, 1.0 ), saturate( g_flVoidCollapse * 1.2 ) );
        float3 emissive = ringColor * ring * bands * ( 2.2 + 1.6 * g_flVoidCharge + 9.0 * g_flVoidCollapse );

        // The core: a hair above true black so it reads as a surface even unlit, with the
        // faintest violet breath deep inside.
        float3 core = g_vVoidColor * ( 0.006 + 0.012 * fresnel * bands );

        return float4( core + emissive, 1.0 );
    }
}
