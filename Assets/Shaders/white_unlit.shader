// Solid white, unlit — the hit-flash look. Ignores all lighting and textures so the flash
// reads identically in daylight, shadow, or night.
// Skeleton from wheatleymf's shader tutorial ("Shader Fundamentals ~ Pt.2"), used with
// permission per its header.

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

    // Flat flash colour — white by default (the classic hit flash); the boss's blocked-hit
    // flash tints it blue via its own vmat. Values may exceed 1 to feed bloom.
    float3 g_vFlashColor < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); >;

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        return float4( g_vFlashColor, 1 );
    }
}
