// Solid red, unlit — the player hit-flash look. Same skeleton as white_unlit.shader.

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
        return float4( 1.0, 0.2, 0.2, 1 );
    }
}
