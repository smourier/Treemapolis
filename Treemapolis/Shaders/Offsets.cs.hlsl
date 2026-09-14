#include "Common.hlsli"

cbuffer Parameters : register(b0)
{
    uint chunkCount;
    uint padding0;
    uint padding1;
    uint padding2;
};

StructuredBuffer<uint> ChunkCounts : register(t0);
RWStructuredBuffer<uint> ChunkOffsets : register(u0);
RWStructuredBuffer<uint> Arguments : register(u1);

// a running sum over the chunks, serial on purpose, a few thousand additions cost nothing and need no parallel prefix sum.
[RootSignature("RootFlags(0), RootConstants(num32BitConstants=4, b0), SRV(t0), UAV(u0), UAV(u1)")]
[numthreads(1, 1, 1)]
void main()
{
    uint total = 0;
    for (uint chunk = 0; chunk < chunkCount; chunk++)
    {
        ChunkOffsets[chunk] = total;
        total += min(ChunkCounts[chunk], CHUNK_SIZE);
    }

    // D3D12_DRAW_ARGUMENTS, 36 vertices for a box, as many instances as survived culling.
    Arguments[0] = 36;
    Arguments[1] = total;
    Arguments[2] = 0;
    Arguments[3] = 0;

    // and 72 vertices for the twelve edges of a box, when the scene is drawn as lines.
    Arguments[4] = 72;
    Arguments[5] = total;
    Arguments[6] = 0;
    Arguments[7] = 0;
}
