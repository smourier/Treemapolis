#include "Ground.hlsli"

static const float3 GroundColor = float3(0.012, 0.02, 0.05);
static const float3 GridColor = float3(0.06, 0.16, 0.3);
static const float GridCell = 4.0;
static const float ShadowDarkness = 0.45;

[RootSignature(GroundRootSignature)]
SceneOutput main(GroundVertex input)
{
    // lines of constant width on screen, faded out where the cells shrink under a pixel and would only alias.
    float2 coordinates = input.worldPosition.xz / GridCell;
    float2 derivative = fwidth(coordinates);
    float2 grid = abs(frac(coordinates - 0.5) - 0.5) / derivative;
    float gridLine = showGrid != 0 ? 1 - saturate(min(grid.x, grid.y)) : 0;
    gridLine *= 1 - saturate(max(derivative.x, derivative.y) * 2);

    // drawn as lines, the ground is as black as the sides of the blocks.
    if (Frame.lineEffect != 0)
    {
        SceneOutput black;
        black.color = float4(0, 0, 0, 1);
        black.entry = 0;
        return black;
    }

    float3 color = lerp(GroundColor, GridColor, gridLine);
    color *= lerp(ShadowDarkness, 1, SampleShadow(Frame, ShadowMap, ShadowSampler, input.worldPosition, float3(0, 1, 0)));
    SceneOutput output;
    output.color = ApplyFog(Frame, color, input.worldPosition);
    output.entry = 0;
    return output;
}
