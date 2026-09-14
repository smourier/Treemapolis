#include "Labels.hlsli"

static const float3 ContainerTextColor = float3(0.8, 0.92, 1.0);
static const float3 FileTextColor = float3(1.0, 1.0, 1.0);
static const float OutlineStrength = 0.7;

[RootSignature(LabelsRootSignature)]
SceneOutput main(LabelVertex input)
{
    // four rotated grid taps across the pixel footprint, text seen at a grazing angle shrinks a lot and would crawl with one.
    float2 dx = ddx(input.uv);
    float2 dy = ddy(input.uv);
    float coverage = 0.25 * (
        Atlas.Sample(LinearSampler, input.uv + 0.125 * dx + 0.375 * dy) +
        Atlas.Sample(LinearSampler, input.uv - 0.125 * dx - 0.375 * dy) +
        Atlas.Sample(LinearSampler, input.uv + 0.375 * dx - 0.125 * dy) +
        Atlas.Sample(LinearSampler, input.uv - 0.375 * dx + 0.125 * dy));

    // a dark halo from the coverage of the neighbours keeps light text readable over light blocks and bright wires.
    float halo = max(max(Atlas.Sample(LinearSampler, input.uv + dx).r, Atlas.Sample(LinearSampler, input.uv - dx).r),
                     max(Atlas.Sample(LinearSampler, input.uv + dy).r, Atlas.Sample(LinearSampler, input.uv - dy).r));

    float3 color = input.kind == LABEL_CONTAINER ? ContainerTextColor : FileTextColor;
    float alpha = max(coverage, saturate(halo * 2) * OutlineStrength);

    // what is fully transparent is not part of the label, neither for the eye nor for picking.
    clip(alpha - 0.02);

    // a name picks what it names, a pedestal is easier to hit by the big text in front of it.
    SceneOutput output;
    output.color = float4(color * coverage, alpha) * (1 - FogAmount(Frame, input.worldPosition));
    output.entry = input.entry + 1;
    return output;
}
