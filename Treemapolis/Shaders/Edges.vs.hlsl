#include "Edges.hlsli"

// the Windows 10 WARP crashes while it builds a shader with an integer table, a return before the end or a choice between vectors,
// so this one is plain arithmetic, a branch is a weight of zero or one and a hidden edge is a position scaled to nothing.

// the two triangles of an edge's quad as bit masks over its six vertices, x from the first corner to the second, y across the line.
static const uint QuadAlongMask = 0x16;
static const uint QuadAcrossMask = 0x34;

static const float NearestW = 0.05;
static const float DepthPullPixels = 2.5;
static const float MaximumPullShare = 0.45;
static const float NewYorkDistanceFade = 2.5;
static const float NewYorkGroundLight = 0.0;
static const float NewYorkCornerFade = 0.1;
static const float NeonDistanceFade = 1.0;
static const float NeonGroundLight = 0.35;
static const float NeonCornerFade = 0.6;
static const float NeonGrey = 0.45;
static const float GreySpread = 0.03;
static const float MinimumSpread = 0.0001;
static const float MinimumDepthSpan = 0.000001;
static const float MinimumLengthPixels = 0.001;
static const float HoverBoost = 1.8;
static const float SelectedBoost = 2.5;
static const float FullEdgePixels = 20;

// the twelve edges of a box, four along each axis, a corner's bits are 1 for +x, 2 for the top and 4 for +z.
// the first corner of an edge spreads its number within the axis over the two bits that are not the axis.
uint FirstCornerOf(uint edge)
{
    uint axis = edge / 4;
    uint step = edge % 4;
    uint low = 1u + (uint)(axis == 0);
    uint high = 4u - 2u * (uint)(axis == 2);
    return (step & 1u) * low + ((step >> 1) & 1u) * high;
}

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
    float spread = high - low;
    return lerp((color - low) / max(spread, MinimumSpread), NeonGrey, (float)(spread < GreySpread));
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
    uint quadVertex = vertexId % 6;
    float2 quad = float2((float)((QuadAlongMask >> quadVertex) & 1u), (float)((QuadAcrossMask >> quadVertex) & 1u) * 2 - 1);
    uint axisBit = 1u << (edge / 4);
    uint cornerA = FirstCornerOf(edge);
    float3 worldA = CornerOf(instance, cornerA);
    float3 worldB = CornerOf(instance, cornerA + axisBit);
    float4 clipA = mul(Frame.viewProjection, float4(worldA, 1));
    float4 clipB = mul(Frame.viewProjection, float4(worldB, 1));

    // an edge that passes behind the camera is cut where it enters the view, one wholly behind it is dropped at the end.
    float behindA = (float)(clipA.w < NearestW);
    float behindB = (float)(clipB.w < NearestW);
    float visible = 1 - behindA * behindB;
    float4 cutA = lerp(clipA, clipB, behindA * (1 - behindB) * (NearestW - clipA.w) / max(clipB.w - clipA.w, MinimumDepthSpan));
    float4 cutB = lerp(clipB, clipA, behindB * (1 - behindA) * (NearestW - clipB.w) / max(clipA.w - clipB.w, MinimumDepthSpan));
    cutA.w = max(cutA.w, NearestW);
    cutB.w = max(cutB.w, NearestW);

    float2 halfViewport = float2(viewportWidth, viewportHeight) * 0.5;
    float2 screenA = cutA.xy / cutA.w * halfViewport;
    float2 screenB = cutB.xy / cutB.w * halfViewport;
    float2 along = screenB - screenA;
    float lengthPixels = length(along);
    float2 direction = along / max(lengthPixels, MinimumLengthPixels);
    float2 normal = float2(-direction.y, direction.x);

    // the quad reaches past both ends by its half width, the pixel shader rounds the stroke off there so corners show no spike.
    float4 clip = lerp(cutA, cutB, quad.x);
    float extend = quad.x * 2 - 1;
    clip.xy += (normal * quad.y + direction * extend) * halfWidth / halfViewport * clip.w;
    float slope = abs(cutB.w - cutA.w) / max(lengthPixels, 1);
    float pull = clip.w * DepthPullPixels / Frame.pixelScale + min(slope * halfWidth * 2, min(instance.size.x, instance.size.z) * MaximumPullShare);
    clip.z *= clip.w / max(clip.w - pull, clip.w * 0.1);

    // the corner nearest the camera is the brightest, each edge walked away from it dims the light, and so does going down the block.
    uint corner = cornerA + axisBit * (uint)quad.x;
    uint nearest = (uint)(Frame.cameraPosition.x > instance.position.x) + (uint)(Frame.cameraPosition.z > instance.position.z) * 4u;
    float steps = (float)countbits((corner ^ nearest) & 5u);
    float up = (float)((corner >> 1) & 1);
    float distance = length(lerp(worldA, worldB, quad.x) - Frame.cameraPosition);

    // an edge only a few pixels long dims, a crowd of tiny blocks would otherwise add up into a solid blot of light.
    float boost = saturate(lengthPixels / (FullEdgePixels * edgeScale));
    boost *= lerp(lerp(1, HoverBoost, (float)(index == Frame.hoveredEntry)), SelectedBoost, (float)(index == Frame.selectedEntry));

    // both looks are worked out and weighed, a value of the one not shown must stay finite.
    float neon = (float)(Frame.lineEffect == LINE_NEON);
    float neonBrightness = lerp(NeonGroundLight, 1, up) * pow(NeonCornerFade, steps) * exp(-distance * Frame.fogDensity * NeonDistanceFade);
    float newYorkBrightness = lerp(NewYorkGroundLight, 1, up) * pow(NewYorkCornerFade, steps) * exp(-distance * Frame.fogDensity * NewYorkDistanceFade);

    EdgeVertex output;
    output.position = clip * visible;
    output.color = lerp(NewYorkGreen, Saturated(UnpackColor(instance.color)), neon);
    output.brightness = lerp(newYorkBrightness, neonBrightness, neon) * boost;
    output.across = quad.y * halfWidth;
    output.along = quad.x * lengthPixels + extend * halfWidth;
    output.lengthPixels = lengthPixels;
    return output;
}
