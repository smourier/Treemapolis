#include "Common.hlsli"

cbuffer Parameters : register(b0)
{
    uint chunkCount;
    uint padding0;
    uint padding1;
    uint padding2;
};

StructuredBuffer<uint> ChunkLists : register(t0);
StructuredBuffer<uint> ChunkCounts : register(t1);
StructuredBuffer<uint> ChunkOffsets : register(t2);
RWStructuredBuffer<uint> Visible : register(u0);

[RootSignature("RootFlags(0), RootConstants(num32BitConstants=4, b0), SRV(t0), SRV(t1), SRV(t2), UAV(u0)")]
[numthreads(64, 1, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint chunk = id.x;
    if (chunk >= chunkCount)
        return;

    // a chunk never holds more than its entries, the bound keeps a bad count from hanging the GPU.
    uint count = min(ChunkCounts[chunk], CHUNK_SIZE);
    uint source = chunk * CHUNK_SIZE;
    uint destination = ChunkOffsets[chunk];
    for (uint i = 0; i < count; i++)
    {
        Visible[destination + i] = ChunkLists[source + i];
    }
}
