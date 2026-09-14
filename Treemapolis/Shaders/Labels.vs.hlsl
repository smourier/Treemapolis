#include "Labels.hlsli"

static const float TopLift = 0.01;
static const float FileLift = 0.08;

[RootSignature(LabelsRootSignature)]
LabelVertex main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    LabelInstance label = Labels[instanceId];
    float2 corner = UnitQuadCorners[vertexId];
    LabelVertex output = (LabelVertex)0;

    // a label for an entry the instance buffer does not hold is dropped, never read past the end of the buffer.
    if (label.entry >= Frame.instanceCount)
    {
        output.position = float4(0, 0, 0, 0);
        return output;
    }

    CurrentInstance anchor = Current[label.entry];

    // the anchor is read from the eased instance, so a label moves with its tile while the map morphs.
    float3 world;
    if (label.kind == LABEL_CONTAINER)
    {
        // flat on the top of the folder, in the band kept free along its front edge.
        // seen from behind, the text turns half a turn inside that same band, so it still reads left to right.
        float top = anchor.position.y + anchor.size.y + TopLift;
        float front = anchor.position.z + anchor.size.z * 0.5 - label.frontInset;
        float2 place = Frame.cameraRight.x < 0 ? 1 - corner : corner;
        world = float3(anchor.position.x + (place.x - 0.5) * label.worldSize.x, top, front - (1 - place.y) * label.worldSize.y);
    }
    else
    {
        float3 top = anchor.position + float3(0, anchor.size.y + FileLift, 0);
        world = top + Frame.cameraRight * (corner.x - 0.5) * label.worldSize.x + Frame.cameraUp * (1 - corner.y) * label.worldSize.y;
    }

    output.worldPosition = world;
    output.position = mul(Frame.viewProjection, float4(world, 1));
    output.uv = label.uvOffset + corner * label.uvSize;
    output.kind = label.kind;
    output.entry = label.entry;
    return output;
}
