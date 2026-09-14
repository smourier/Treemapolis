#include "Common.hlsli"

cbuffer Parameters : register(b1)
{
    uint instanceCount;
    uint chunkCount;
    uint padding0;
    uint padding1;
};

ConstantBuffer<FrameConstants> Frame : register(b0);
StructuredBuffer<CurrentInstance> Current : register(t0);
RWStructuredBuffer<uint> ChunkLists : register(u0);
RWStructuredBuffer<uint> ChunkCounts : register(u1);

// one thread per chunk writes into its own region of the list, so no two threads ever touch the same element.
[RootSignature("RootFlags(0), CBV(b0), RootConstants(num32BitConstants=4, b1), SRV(t0), UAV(u0), UAV(u1)")]
[numthreads(64, 1, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint chunk = id.x;
    if (chunk >= chunkCount)
        return;

    uint first = chunk * CHUNK_SIZE;
    uint last = min(first + CHUNK_SIZE, instanceCount);
    uint written = 0;
    for (uint i = first; i < last; i++)
    {
        CurrentInstance instance = Current[i];
        if ((instance.flags & INSTANCE_VISIBLE) == 0)
            continue;

        float3 extents = instance.size * 0.5;
        if (IsBoxVisible(Frame, instance.position + float3(0, extents.y, 0), extents))
        {
            ChunkLists[first + written] = i;
            written++;
        }
    }
    ChunkCounts[chunk] = written;
}
