#include "Ground.hlsli"

static const float2 Corners[6] = { float2(0, 0), float2(0, 1), float2(1, 1), float2(0, 0), float2(1, 1), float2(1, 0) };

[RootSignature(GroundRootSignature)]
GroundVertex main(uint vertexId : SV_VertexID)
{
    float2 xz = lerp(groundMin, groundMax, Corners[vertexId]);
    GroundVertex output;
    output.worldPosition = float3(xz.x, 0, xz.y);
    output.position = mul(Frame.viewProjection, float4(output.worldPosition, 1));
    return output;
}
