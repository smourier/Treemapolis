#ifndef TREEMAPOLIS_GROUND
#define TREEMAPOLIS_GROUND

#include "Common.hlsli"
#include "Shadow.hlsli"

#define GroundRootSignature "RootFlags(0), CBV(b0), RootConstants(num32BitConstants=5, b1), DescriptorTable(SRV(t0)), " SHADOW_SAMPLER

ConstantBuffer<FrameConstants> Frame : register(b0);
Texture2D<float> ShadowMap : register(t0);
SamplerComparisonState ShadowSampler : register(s1);

cbuffer Ground : register(b1)
{
    float2 groundMin;
    float2 groundMax;
    uint showGrid;
};

struct GroundVertex
{
    float4 position : SV_Position;
    float3 worldPosition : POSITION;
};

#endif
