// Solid heal-green, unlit — the player heal-flash look. Same skeleton as
// red_unlit.shader, tinted to the heal palette's core green.

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

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        return float4( 0.25, 1.0, 0.35, 1 );
    }
}
