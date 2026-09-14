#ifndef TREEMAPOLIS_EDGES
#define TREEMAPOLIS_EDGES

#include "Common.hlsli"

#define EdgesRootSignature "RootFlags(0), CBV(b0), SRV(t0), SRV(t1), RootConstants(num32BitConstants=4, b1)"

ConstantBuffer<FrameConstants> Frame : register(b0);
StructuredBuffer<uint> Visible : register(t0);
StructuredBuffer<CurrentInstance> Current : register(t1);

cbuffer Edges : register(b1)
{
    float viewportWidth;
    float viewportHeight;

    // how far the quad of an edge reaches on each side of the line, in pixels, the glow included.
    float halfWidth;
    float edgeScale;
};

struct EdgeVertex
{
    float4 position : SV_Position;

    // pixels across the line and along it from its first end, and how long it is, for the round ends of the stroke.
    noperspective float across : ACROSS;
    noperspective float along : ALONG;
    nointerpolation float lengthPixels : LENGTH;
    float brightness : BRIGHTNESS;
    nointerpolation float3 color : COLOR;
};

#endif
