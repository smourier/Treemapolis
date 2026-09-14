#include "Post.hlsli"

// one triangle trick
[RootSignature(PostRootSignature)]
PostVertex main(uint vertexId : SV_VertexID)
{
    float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
    PostVertex output;
    output.position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    output.uv = uv;
    return output;
}
