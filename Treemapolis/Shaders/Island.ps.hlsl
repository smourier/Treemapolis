#include "Island.hlsli"

static const float RimWidth = 0.08;
static const float OutlinePixels = 1.25;
static const float HoverTint = 0.1;
static const float3 GlowColor = float3(0.45, 0.85, 1.0);
static const float3 SelectedColor = float3(1.0, 0.84, 0.5);

[RootSignature(IslandRootSignature)]
SceneOutput main(IslandVertex input)
{
    float3 normal = normalize(input.normal);
    float diffuse = saturate(dot(normal, -Frame.lightDirection));
    float3 ambient = lerp(float3(0.08, 0.07, 0.07), float3(0.3, 0.34, 0.42), normal.y * 0.5 + 0.5);
    float2 faceUv = input.uv * input.faceSize;
    float2 edge = min(input.uv, 1 - input.uv) * input.faceSize;
    float rim = smoothstep(0.0, RimWidth, min(edge.x, edge.y));
    float3 color = input.color * (ambient + diffuse * float3(1.0, 0.95, 0.88)) * lerp(0.55, 1.0, rim);

    // the outlines are measured in pixels along each side on its own, so they stay one line wide and meet squarely in the corners.
    float2 edgePixels = edge / max(fwidth(faceUv), 0.00001);
    float outline = normal.y > 0.5 ? 1 - smoothstep(OutlinePixels, OutlinePixels + 1, min(edgePixels.x, edgePixels.y)) : 0;

    // the tile under the pointer or just clicked brightens as a whole and draws a fine light line around its top,
    // the tile the map shows keeps a warm one.
    color += GlowColor * input.glow * HoverTint;
    color = lerp(color, GlowColor, saturate(input.glow * 2) * outline * 0.7);
    color = lerp(color + SelectedColor * 0.03 * input.selected, SelectedColor, input.selected * outline * 0.85);

    SceneOutput output;
    output.color = float4(color, 1);
    output.entry = 0;
    return output;
}
