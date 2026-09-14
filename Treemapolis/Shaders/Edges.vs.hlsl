#include "Edges.hlsli"

// the twelve edges of a box as pairs of corners, a corner's bits are 1 for +x, 2 for the top and 4 for +z.
static const uint EdgeCorners[24] = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };

// the two triangles of an edge's quad, x from the first corner to the second, y across the line.
static const float2 EdgeQuad[6] = { float2(0, -1), float2(1, -1), float2(1, 1), float2(0, -1), float2(1, 1), float2(0, 1) };

static const float NearestW = 0.05;
static const float DepthPullPixels = 2.5;
static const float MaximumPullShare = 0.45;
static const float NewYorkDistanceFade = 2.5;
static const float NewYorkGroundLight = 0.0;
static const float NewYorkHeightCurve = 3.0;
static const float NewYorkCornerFade = 0.1;
static const float NeonDistanceFade = 1.0;
static const float NeonGroundLight = 0.35;
static const float NeonCornerFade = 0.6;
static const float NeonGrey = 0.45;
static const float HoverBoost = 1.8;
static const float SelectedBoost = 2.5;
static const float FullEdgePixels = 20;

float3 CornerOf(CurrentInstance instance, uint corner)
{
    float3 local = float3((float)(corner & 1) - 0.5, (float)((corner >> 1) & 1), (float)((corner >> 2) & 1) - 0.5);
    return instance.position + local * instance.size;
}

// the block color with its grey taken out, so neon tubes are vivid and tell the kinds apart, a grey block stays a dim grey.
float3 Saturated(float3 color)
{
    float high = max(color.r, max(color.g, color.b));
    float low = min(color.r, min(color.g, color.b));
    if (high - low < 0.03)
        return NeonGrey;

    return (color - low) / (high - low);
}

// an edge becomes a quad a few pixels wide in screen space, pulled towards the camera so the faces it borders never hide its glow.
// an edge running away from the camera changes depth fast along the screen, so it is pulled by as much depth as its glow spans,
// never by more than part of the block, the edges on its far side stay hidden.
[RootSignature(EdgesRootSignature)]
EdgeVertex main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    uint index = Visible[instanceId];
    CurrentInstance instance = Current[index];
    uint edge = vertexId / 6;
    float2 quad = EdgeQuad[vertexId % 6];
    uint cornerA = EdgeCorners[edge * 2];
    uint cornerB = EdgeCorners[edge * 2 + 1];
    float3 worldA = CornerOf(instance, cornerA);
    float3 worldB = CornerOf(instance, cornerB);
    float4 clipA = mul(Frame.viewProjection, float4(worldA, 1));
    float4 clipB = mul(Frame.viewProjection, float4(worldB, 1));

    EdgeVertex output = (EdgeVertex)0;
    if (clipA.w < NearestW && clipB.w < NearestW)
        return output;

    // an edge that passes behind the camera is cut where it enters the view.
    if (clipA.w < NearestW)
    {
        clipA = lerp(clipA, clipB, (NearestW - clipA.w) / (clipB.w - clipA.w));
    }
    else if (clipB.w < NearestW)
    {
        clipB = lerp(clipB, clipA, (NearestW - clipB.w) / (clipA.w - clipB.w));
    }

    float2 halfViewport = float2(viewportWidth, viewportHeight) * 0.5;
    float2 screenA = clipA.xy / clipA.w * halfViewport;
    float2 screenB = clipB.xy / clipB.w * halfViewport;
    float2 along = screenB - screenA;
    float lengthPixels = length(along);
    float2 direction = lengthPixels > 0.001 ? along / lengthPixels : float2(1, 0);
    float2 normal = float2(-direction.y, direction.x);

    // the quad reaches past both ends by its half width, the pixel shader rounds the stroke off there so corners show no spike.
    bool second = quad.x > 0.5;
    float4 clip = second ? clipB : clipA;
    float extend = quad.x * 2 - 1;
    clip.xy += (normal * quad.y + direction * extend) * halfWidth / halfViewport * clip.w;
    float slope = abs(clipB.w - clipA.w) / max(lengthPixels, 1);
    float pull = clip.w * DepthPullPixels / Frame.pixelScale + min(slope * halfWidth * 2, min(instance.size.x, instance.size.z) * MaximumPullShare);
    clip.z *= clip.w / max(clip.w - pull, clip.w * 0.1);

    // the corner nearest the camera is the brightest, each edge walked away from it dims the light, and so does going down the block.
    uint corner = second ? cornerB : cornerA;
    uint nearest = (Frame.cameraPosition.x > instance.position.x ? 1u : 0u) | (Frame.cameraPosition.z > instance.position.z ? 4u : 0u);
    float steps = (float)countbits((corner ^ nearest) & 5u);
    float up = (float)((corner >> 1) & 1);
    float distance = length((second ? worldB : worldA) - Frame.cameraPosition);

    // an edge only a few pixels long dims, a crowd of tiny blocks would otherwise add up into a solid blot of light.
    float boost = saturate(lengthPixels / (FullEdgePixels * edgeScale));
    boost *= index == Frame.selectedEntry ? SelectedBoost : index == Frame.hoveredEntry ? HoverBoost : 1;
    if (Frame.lineEffect == LINE_NEON)
    {
        output.color = Saturated(UnpackColor(instance.color));
        output.brightness = lerp(NeonGroundLight, 1, up) * pow(NeonCornerFade, steps) * exp(-distance * Frame.fogDensity * NeonDistanceFade) * boost;
    }
    else
    {
        output.color = NewYorkGreen;
        output.brightness = lerp(NewYorkGroundLight, 1, pow(up, NewYorkHeightCurve)) * pow(NewYorkCornerFade, steps) * exp(-distance * Frame.fogDensity * NewYorkDistanceFade) * boost;
    }

    output.position = clip;
    output.across = quad.y * halfWidth;
    output.along = quad.x * lengthPixels + extend * halfWidth;
    output.lengthPixels = lengthPixels;
    return output;
}
