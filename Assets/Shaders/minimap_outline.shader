// The minimap outline (Code/UI/DungeonMinimap.cs): every slab also carries an inverted hull grown by
// a few units, drawn with front faces culled so only its far inside shows around the silhouette,
// the same trick as heden_outline.shader. Neighbouring cells of one tier hide each other's hull, so
// only the outer edge of a room gets a line. Same dithered circular cutoff as minimap_unlit.shader.

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

    RenderState( CullMode, FRONT );

    // Same blend state as minimap_unlit.shader so the hull stays in the translucent bucket next to the
    // body it belongs to. A solid piece is order-independent anyway: alpha 1 replaces, and the hull top
    // sits a unit below the body top so depth settles it whichever draws first.
    RenderState( DepthWriteEnable, true );
    RenderState( BlendEnable, true );
    RenderState( SrcBlend, SRC_ALPHA );
    RenderState( DstBlend, INV_SRC_ALPHA );
    RenderState( SrcBlendAlpha, ONE );
    RenderState( DstBlendAlpha, INV_SRC_ALPHA );

    BoolAttribute( translucent, true );

    float3 g_vMinimapOutlineColor < UiType( Color ); Default3( 0.03, 0.02, 0.05 ); UiGroup( "Minimap,10/10" ); >;
    float g_flMinimapEdgeStart < Default( 0.40 ); Range( 0.0, 1.0 ); UiGroup( "Minimap,10/20" ); >;
    float g_flMinimapEdgeEnd < Default( 0.49 ); Range( 0.0, 1.0 ); UiGroup( "Minimap,10/21" ); >;
    // Matches minimap_unlit.shader: the hull is a second mesh on the same renderer, so it takes the
    // same attribute. Only used to drop the hull once the piece starts blending, see MainPs.
    float g_flMinimapOpacity < Attribute( "MinimapOpacity" ); Default( 1.0 ); >;

    float MinimapBayer( float2 px )
    {
        int2 p = int2( floor( px ) ) & 3;
        const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        return ( m[p.y * 4 + p.x] + 0.5 ) / 16.0;
    }

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        // A piece that blends drops its outline altogether. The hull lies under the body rather than
        // around it (grown sideways and DOWN), so once the body stops being opaque a hull drawn behind
        // it would just darken the whole slab instead of ringing it. Losing the ring on the floors
        // above is the point anyway: those black rings are what made the stack busy to read.
        clip( g_flMinimapOpacity - 0.999 );

        float2 ss = i.vPositionSs.xy;
        float2 c = ( ss - g_vViewportSize * 0.5 ) / g_vViewportSize.x;
        float fade = 1.0 - smoothstep( g_flMinimapEdgeStart, g_flMinimapEdgeEnd, length( c ) );
        clip( fade - MinimapBayer( ss ) );
        return float4( g_vMinimapOutlineColor, 1 );
    }
}
