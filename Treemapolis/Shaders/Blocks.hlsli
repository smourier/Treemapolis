#ifndef TREEMAPOLIS_BLOCKS
#define TREEMAPOLIS_BLOCKS

#include "Common.hlsli"
#include "Shadow.hlsli"

#define BlocksRootSignature "RootFlags(0), CBV(b0), SRV(t0), SRV(t1), SRV(t2), SRV(t3), DescriptorTable(SRV(t4)), SRV(t5), DescriptorTable(SRV(t6)), StaticSampler(s0, filter=FILTER_MIN_MAG_MIP_LINEAR, addressU=TEXTURE_ADDRESS_CLAMP, addressV=TEXTURE_ADDRESS_CLAMP), " SHADOW_SAMPLER

// mirror the constants of Rendering\ThumbnailAtlas.cs and Shell\ThumbnailLoader.cs.
#define THUMBNAIL_CELLS_PER_ROW 16
#define THUMBNAIL_CELLS_PER_SLICE 256
#define THUMBNAIL_LEVELS 5
#define THUMBNAIL_ATLAS_SIZE 2048.0

ConstantBuffer<FrameConstants> Frame : register(b0);
StructuredBuffer<uint> Visible : register(t0);
StructuredBuffer<CurrentInstance> Current : register(t1);
StructuredBuffer<uint> EntryCells : register(t2);
StructuredBuffer<float2> CellExtents : register(t3);
Texture2DArray<float4> Thumbnails : register(t4);
StructuredBuffer<TargetInstance> Targets : register(t5);
Texture2D<float> ShadowMap : register(t6);
SamplerState LinearSampler : register(s0);
SamplerComparisonState ShadowSampler : register(s1);

struct BlockVertex
{
    float4 position : SV_Position;
    float3 worldPosition : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float2 faceSize : TEXCOORD1;
    nointerpolation float3 color : COLOR;
    nointerpolation float rimWidth : RIM;
    nointerpolation uint flags : FLAGS;
    nointerpolation uint entry : ENTRY;

    // the color of a recent change and how strongly it shows right now.
    nointerpolation float4 change : CHANGE;
};

#endif
