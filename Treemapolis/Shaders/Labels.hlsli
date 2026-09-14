#ifndef TREEMAPOLIS_LABELS
#define TREEMAPOLIS_LABELS

#include "Common.hlsli"

#define LABEL_CONTAINER 0
#define LABEL_FILE 1

#define LabelsRootSignature "RootFlags(0), CBV(b0), SRV(t0), SRV(t1), DescriptorTable(SRV(t2)), StaticSampler(s0, filter=FILTER_MIN_MAG_MIP_LINEAR, addressU=TEXTURE_ADDRESS_CLAMP, addressV=TEXTURE_ADDRESS_CLAMP)"

// mirrors LabelInstance in Rendering\LabelInstance.cs, field for field.
struct LabelInstance
{
    uint entry;
    uint kind;
    float2 uvOffset;
    float2 uvSize;
    float2 worldSize;
    float frontInset;
    float padding;
};

ConstantBuffer<FrameConstants> Frame : register(b0);
StructuredBuffer<LabelInstance> Labels : register(t0);
StructuredBuffer<CurrentInstance> Current : register(t1);
Texture2D<float> Atlas : register(t2);
SamplerState LinearSampler : register(s0);

struct LabelVertex
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD;
    float3 worldPosition : POSITION;
    nointerpolation uint kind : KIND;
    nointerpolation uint entry : ENTRY;
};

#endif
