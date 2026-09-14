#include "Common.hlsli"

cbuffer Parameters : register(b0)
{
    uint instanceCount;
    float blend;
    uint padding0;
    uint padding1;
};

StructuredBuffer<TargetInstance> Targets : register(t0);
RWStructuredBuffer<CurrentInstance> Current : register(u0);

[RootSignature("RootFlags(0), RootConstants(num32BitConstants=4, b0), SRV(t0), UAV(u0)")]
[numthreads(1024, 1, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= instanceCount)
        return;

    TargetInstance target = Targets[id.x];
    CurrentInstance current = Current[id.x];

    // a slot seen for the first time starts flat where it belongs and grows out of the ground.
    if ((current.flags & INSTANCE_INITIALIZED) == 0)
    {
        current.position = target.position;
        current.size = float3(target.size.x, 0, target.size.z);
    }

    // an entry that leaves the map stays where it was, so it grows back from there when it returns, a dive and its way back out being the same morph.
    if ((target.flags & INSTANCE_VISIBLE) != 0)
    {
        current.position = lerp(current.position, target.position, blend);
        current.size = lerp(current.size, target.size, blend);
    }
    current.color = target.color;
    current.flags = target.flags | INSTANCE_INITIALIZED;
    Current[id.x] = current;
}
