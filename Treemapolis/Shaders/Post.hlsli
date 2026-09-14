#ifndef TREEMAPOLIS_POST
#define TREEMAPOLIS_POST

#define PostRootSignature "RootFlags(0), RootConstants(num32BitConstants=8, b0), DescriptorTable(SRV(t0)), DescriptorTable(SRV(t1)), StaticSampler(s0, filter=FILTER_MIN_MAG_MIP_POINT, addressU=TEXTURE_ADDRESS_CLAMP, addressV=TEXTURE_ADDRESS_CLAMP)"

// mirror the post effects of ScreenEffect in Configuration\ScreenEffect.cs, the others are drawn in the scene.
#define EFFECT_CARTOON 1
#define EFFECT_PIXEL_ART 2

cbuffer Post : register(b0)
{
    uint effect;
    float width;
    float height;

    // the monitor scale, so lines and pixels keep their size on a dense screen.
    float scale;

    // the island is part of the scene but it is not the map, the effect leaves its rectangle as it is.
    float2 islandMin;
    float2 islandMax;
};

Texture2D<float4> Scene : register(t0);
Texture2D<uint> Entries : register(t1);
SamplerState PointSampler : register(s0);

struct PostVertex
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

#endif
