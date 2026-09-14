#include "Common.hlsli"

#define ShadowRootSignature "RootFlags(0), CBV(b0), SRV(t0), SRV(t1)"

ConstantBuffer<FrameConstants> Frame : register(b0);
StructuredBuffer<uint> Visible : register(t0);
StructuredBuffer<CurrentInstance> Current : register(t1);

// the blocks as the sun sees them, depth only, from the list its own culling pass kept.
[RootSignature(ShadowRootSignature)]
float4 main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID) : SV_Position
{
    CurrentInstance instance = Current[Visible[instanceId]];
    float3 local;
    float3 normal;
    float2 uv;
    GetBoxVertex(vertexId, local, normal, uv);
    return mul(Frame.viewProjection, float4(instance.position + local * instance.size, 1));
}
