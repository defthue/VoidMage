// The minimap slabs (Code/UI/DungeonMinimap.cs). Flat vertex colour with a fixed-to-the-panel toon
// shade (the normal comes from screen derivatives, so the flat slabs need no vertex normals): top
// faces bright, the face toward the viewer mid, the rest dark, quantised to four tones. The map
// renders into its own small render target; a Bayer-dithered circular cutoff on that target's pixel
// grid gives the compass edge in the same pixel language as the retro filter. No fog, no lighting,
// no tonemapping path. Two-sided, so mesh winding never matters.
// The standard vertex input carries no colour; the Color semantic is declared here the way
// foliage.shader does. Skeleton from white_unlit.shader (wheatleymf's shader tutorial, used with
// permission per its header).

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

    float4 vColor : COLOR0 < Semantic( Color ); >;
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
        o.vVertexColor = i.vColor;
        return FinalizeVertex( o );
    }
}

PS
{
    #define CUSTOM_MATERIAL_INPUTS
    #include "common/pixel.hlsl"

    RenderState( CullMode, NONE );

    // Alpha blended, so the floors above the player can be seen through. Depth write stays ON, unlike
    // a particle shader: it is what stops a slab blending against its own back faces (the top face is
    // first in the index buffer, so it wins and the bottom and far side faces fail the test) and what
    // keeps the outline hull outside the body silhouette. A lower floor still shows through because
    // the translucent bucket draws back to front and the map camera looks down, so the floors below
    // are the far ones and have already drawn by the time a faded floor blends over them.
    RenderState( DepthWriteEnable, true );
    RenderState( BlendEnable, true );
    RenderState( SrcBlend, SRC_ALPHA );
    RenderState( DstBlend, INV_SRC_ALPHA );
    // The map is a transparent render target the HUD composites as STRAIGHT alpha (core ui/pixel.hlsl:
    // "Straight-alpha output"), so the alpha channel has to keep meaning coverage rather than picking up
    // the RGB blend. This is the pair the engine's own sprite/glass shaders use for that.
    RenderState( SrcBlendAlpha, ONE );
    RenderState( DstBlendAlpha, INV_SRC_ALPHA );

    BoolAttribute( translucent, true );

    // Darkest tone a face can take (top faces are always 1).
    float g_flMinimapShadeMin < Default( 0.42 ); Range( 0.0, 1.0 ); UiGroup( "Minimap,10/10" ); >;
    // Circular cutoff, as a fraction of the render target's width from its centre: full up to Start,
    // dithered away by End. 0.5 touches the panel sides; raise both past 0.71 for a square map.
    float g_flMinimapEdgeStart < Default( 0.40 ); Range( 0.0, 1.0 ); UiGroup( "Minimap,10/20" ); >;
    float g_flMinimapEdgeEnd < Default( 0.49 ); Range( 0.0, 1.0 ); UiGroup( "Minimap,10/21" ); >;
    // Per-object see-through, set by DungeonMinimap for the levels above the player's: 1 draws solid,
    // lower values blend, so the floor the player is actually standing on reads through the stack.
    float g_flMinimapOpacity < Attribute( "MinimapOpacity" ); Default( 1.0 ); >;

    float MinimapBayer( float2 px )
    {
        int2 p = int2( floor( px ) ) & 3;
        const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
        return ( m[p.y * 4 + p.x] + 0.5 ) / 16.0;
    }

    // Keeps the map circle: fully inside passes, the rim dissolves with an ordered dither.
    void MinimapEdgeClip( float2 ss )
    {
        float2 c = ( ss - g_vViewportSize * 0.5 ) / g_vViewportSize.x;
        float fade = 1.0 - smoothstep( g_flMinimapEdgeStart, g_flMinimapEdgeEnd, length( c ) );
        clip( fade - MinimapBayer( ss ) );
    }

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        MinimapEdgeClip( i.vPositionSs.xy );

        // A real outward normal off the vertex, not one recovered from screen derivatives: a side wall is
        // about one map pixel tall, so a derivative quad over one straddles two faces and the normal it
        // gives is noise that changes as the map turns.
        float3 n = normalize( i.vNormalWs );

        // A blended piece keeps only its top. Every cell of a tier is its own closed prism, so the walls
        // where two cells meet stand INSIDE the room: while the slab is opaque the tops in front of them
        // simply overwrite them, but once it blends they tint every cell boundary and those dark lines
        // swamp the floors the see-through was meant to reveal. Tops point up, a stair ramp still leans
        // only 27 degrees, and walls and undersides do not, so this one test separates them exactly.
        // Shade is left flat as well: a ghost floor is a plain silhouette, and nothing about how it draws
        // then depends on the view, so turning the map cannot make it shimmer.
        if ( g_flMinimapOpacity < 0.999 )
        {
            clip( n.z - 0.7 );
            return float4( SrgbGammaToLinear( i.vVertexColor.rgb ), g_flMinimapOpacity );
        }

        // a lamp fixed to the panel: mostly from the viewer, tilted to the sky and a little to the right,
        // so the tone of a face never changes as the map turns with the player
        float3 fwd = g_vCameraDirWs;
        float3 right = cross( fwd, float3( 0, 0, 1 ) );
        right = length( right ) > 0.01 ? normalize( right ) : float3( 0, 1, 0 );
        float3 lightDir = normalize( -fwd + float3( 0, 0, 0.8 ) + right * 0.35 );

        float t = saturate( dot( n, lightDir ) );
        t = floor( t * 3.0 + 0.5 ) / 3.0;
        float shade = lerp( g_flMinimapShadeMin, 1.0, t );

        float3 color = SrgbGammaToLinear( i.vVertexColor.rgb ) * shade;
        return float4( color, g_flMinimapOpacity );
    }
}
