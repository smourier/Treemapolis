#include "Island.hlsli"

[RootSignature(IslandRootSignature)]
IslandVertex main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    IslandInstance instance = Instances[instanceId];
    float3 local;
    float3 normal;
    float2 uv;
    GetBoxVertex(vertexId, local, normal, uv);
    uint face = vertexId / 6;

    IslandVertex output;
    float3 world = instance.position + float3(0, instance.lift, 0) + local * instance.size;
    output.position = mul(Frame.viewProjection, float4(world, 1));
    output.normal = normal;
    output.uv = uv;
    output.faceSize = float2(dot(abs(FaceU[face]), instance.size), dot(abs(FaceV[face]), instance.size));
    output.color = UnpackColor(instance.color);
    output.glow = instance.glow;
    output.selected = instance.selected;
    return output;
}
