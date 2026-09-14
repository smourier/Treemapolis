#ifndef TREEMAPOLIS_ISLAND
#define TREEMAPOLIS_ISLAND

#include "Common.hlsli"

#define IslandRootSignature "RootFlags(0), CBV(b0), SRV(t0)"

// mirrors IslandInstance in Rendering\IslandInstance.cs, field for field.
struct IslandInstance
{
    float3 position;
    uint color;
    float3 size;
    float lift;
    float glow;
    float selected;
};

ConstantBuffer<FrameConstants> Frame : register(b0);
StructuredBuffer<IslandInstance> Instances : register(t0);

struct IslandVertex
{
    float4 position : SV_Position;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float2 faceSize : TEXCOORD1;
    nointerpolation float3 color : COLOR;
    nointerpolation float glow : GLOW;
    nointerpolation float selected : SELECTED;
};

#endif
