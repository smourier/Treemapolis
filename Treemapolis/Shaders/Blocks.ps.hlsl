#include "Blocks.hlsli"

static const float3 HoverTint = float3(0.16, 0.18, 0.2);
static const float3 SelectionGlow = float3(0.35, 0.9, 1.0);
static const float TopLightening = 1.25;
static const float CollapsedStripes = 10;
static const float AggregateStripes = 6;
static const float ImageMargin = 1.5;
static const float GroundShade = 0.35;
static const float SideSkyLight = 1.8;
static const float NeonFill = 0.01;
static const float NeonPicture = 0.35;
static const float NewYorkPicture = 0.3;
static const float3 LumaWeights = float3(0.2126, 0.7152, 0.0722);

// the picture of an image file, fitted inside the rim of the top face with the block color around it.
// it reads upright from the front, and turns half a turn when seen from behind, as the names do.
float3 ApplyThumbnail(uint cell, float2 faceUv, float2 faceSize, float margin, float2 uvDx, float2 uvDy, float3 background)
{
    float2 extent = CellExtents[cell];
    float2 inner = max(faceSize - 2 * margin, 0.0001);
    float imageAspect = extent.x / max(extent.y, 0.0001);
    float faceAspect = inner.x / inner.y;
    float2 fit = faceAspect > imageAspect ? float2(imageAspect / faceAspect, 1) : float2(1, faceAspect / imageAspect);
    float2 local = (faceUv * faceSize - margin) / inner;
    local.y = 1 - local.y;
    if (Frame.cameraRight.x < 0)
    {
        local = 1 - local;
    }

    float2 picture = (local - 0.5) / fit + 0.5;

    // the edge of the picture is not an edge of the geometry, so no multisampling smooths it,
    // its coverage is taken from how much of the picture one pixel spans, from the face uv derivatives rather than from ddx in a branch.
    float2 pictureWidth = (abs(uvDx) + abs(uvDy)) * faceSize / inner / fit;
    float2 edge = min(picture, 1 - picture) / max(pictureWidth, 0.00001);
    float coverage = saturate(min(edge.x, edge.y) + 0.5);
    if (coverage <= 0)
        return background;

    // the level is chosen from how fast the face uv moves across the pixel, and the sample is kept inside the cell at that level.
    float cellScale = 1.0 / THUMBNAIL_CELLS_PER_ROW;
    uint index = cell % THUMBNAIL_CELLS_PER_SLICE;
    float2 origin = float2(index % THUMBNAIL_CELLS_PER_ROW, index / THUMBNAIL_CELLS_PER_ROW) * cellScale;
    float2 scale = faceSize / inner / fit * extent * cellScale;
    float footprint = max(length(uvDx * scale), length(uvDy * scale)) * THUMBNAIL_ATLAS_SIZE;
    float level = clamp(log2(max(footprint, 1)), 0, THUMBNAIL_LEVELS - 1);
    float inset = 0.5 * pow(2, ceil(level)) / THUMBNAIL_ATLAS_SIZE;
    float2 uv = clamp(origin + picture * extent * cellScale, origin + inset, origin + extent * cellScale - inset);
    float4 texel = Thumbnails.SampleLevel(LinearSampler, float3(uv, cell / THUMBNAIL_CELLS_PER_SLICE), level);
    return lerp(background, texel.rgb + background * (1 - texel.a), coverage);
}

[RootSignature(BlocksRootSignature)]
SceneOutput main(BlockVertex input)
{

    float3 normal = normalize(input.normal);
    float2 uvDx = ddx(input.uv);
    float2 uvDy = ddy(input.uv);
    float diffuse = saturate(dot(normal, -Frame.lightDirection));
    if (diffuse > 0)
    {
        diffuse *= SampleShadow(Frame, ShadowMap, ShadowSampler, input.worldPosition, normal);
    }
    float sky = normal.y * 0.5 + 0.5;
    float3 ambient = lerp(float3(0.06, 0.05, 0.05), float3(0.22, 0.27, 0.36), sky);

    float2 edge = min(input.uv, 1 - input.uv) * input.faceSize;
    float edgeDistance = min(edge.x, edge.y);
    float rim = smoothstep(0.0, input.rimWidth, edgeDistance);
    float3 albedo = input.color;
    bool top = normal.y > 0.5;

    // drawn as lines, a block is a dark shape that hides what is behind it, a picture on top shows through faintly,
    // in its own colors for neon and as green on the display of New York.
    if (Frame.lineEffect != 0)
    {
        float3 fill = Frame.lineEffect == LINE_NEON ? input.color * NeonFill : (float3)0;
        uint pictureCell = top && Frame.showThumbnails != 0 && (input.flags & (INSTANCE_CONTAINER | INSTANCE_COLLAPSED | INSTANCE_AGGREGATE)) == 0 ? EntryCells[input.entry] : 0;
        if (pictureCell != 0)
        {
            float3 picture = ApplyThumbnail(pictureCell - 1, input.uv, input.faceSize, input.rimWidth * ImageMargin, uvDx, uvDy, fill);
            fill = Frame.lineEffect == LINE_NEON ? picture * NeonPicture : NewYorkGreen * dot(picture, LumaWeights) * NewYorkPicture;
        }

        SceneOutput dark;
        dark.color = ApplyFog(Frame, fill, input.worldPosition);
        dark.entry = input.entry + 1;
        return dark;
    }

    // a folder too small to show what it holds is hatched on top, the block that gathers small items is striped.
    if (top && (input.flags & INSTANCE_COLLAPSED) != 0)
    {
        albedo *= lerp(0.8, 1.1, step(0.5, frac((input.uv.x + input.uv.y) * CollapsedStripes)));
    }
    else if (top && (input.flags & INSTANCE_AGGREGATE) != 0)
    {
        albedo *= lerp(0.75, 1.05, step(0.5, frac(input.uv.x * AggregateStripes)));
    }
    else if (top && (input.flags & INSTANCE_CONTAINER) != 0)
    {
        albedo *= TopLightening;
    }
    else if (top && Frame.showThumbnails != 0)
    {
        uint cell = EntryCells[input.entry];
        if (cell != 0)
        {
            albedo = ApplyThumbnail(cell - 1, input.uv, input.faceSize, input.rimWidth * ImageMargin, uvDx, uvDy, albedo);
        }
    }

    // the sides darken from their top down to where the block stands, a shade of their own whatever the sun does.
    // the sky light itself fades, so a side the sun does not reach shows it as clearly as a lit one, v runs from the bottom edge up.
    float sunShade = 1;
    float skyShade = 1;
    if (abs(normal.y) < 0.5)
    {
        float up = pow(input.uv.y, 0.7);
        sunShade = lerp(GroundShade, 1, up);
        skyShade = lerp(GroundShade, SideSkyLight, up);
    }
    float3 color = albedo * (ambient * skyShade + diffuse * float3(1.0, 0.95, 0.85) * sunShade) * lerp(0.5, 1.0, rim);
    if (input.entry == Frame.hoveredEntry)
    {
        color += HoverTint;
    }

    // a change lights the whole block and burns brighter along its edges.
    if (input.change.a > 0)
    {
        float changeEdge = 1 - smoothstep(0.0, input.rimWidth * 4, edgeDistance);
        color = lerp(color, input.change.rgb, input.change.a * 0.7) + input.change.rgb * input.change.a * (0.3 + 1.2 * changeEdge);
    }

    // the selection breathes along its edges, bright enough to find again from far away.
    if (input.entry == Frame.selectedEntry)
    {
        float pulse = 0.65 + 0.35 * sin(Frame.time * 4);
        float glow = 1 - smoothstep(0.0, input.rimWidth * 3, edgeDistance);
        color = lerp(color, SelectionGlow, glow * pulse * 0.8) + SelectionGlow * 0.06;
    }

    SceneOutput output;
    output.color = ApplyFog(Frame, color, input.worldPosition);
    output.entry = input.entry + 1;
    return output;
}
