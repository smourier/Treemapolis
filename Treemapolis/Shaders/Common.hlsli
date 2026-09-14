#ifndef TREEMAPOLIS_COMMON
#define TREEMAPOLIS_COMMON

// entries are culled in chunks of this many, each chunk by one compute thread, which needs no atomic counter anywhere.
#define CHUNK_SIZE 1024

#define INSTANCE_VISIBLE 0x1
#define INSTANCE_CONTAINER 0x2
#define INSTANCE_HIDDEN 0x8
#define INSTANCE_DRIVE 0x10
#define INSTANCE_COLLAPSED 0x20
#define INSTANCE_AGGREGATE 0x40
#define INSTANCE_INITIALIZED 0x80000000u

#define CHANGE_ADDED 1
#define CHANGE_CHANGED 2
#define CHANGE_REMOVED 3
#define CHANGE_KIND_SHIFT 30
#define CHANGE_TIME_MASK 0x3FFFFFFFu
#define CHANGE_SECONDS 3.0
#define NO_ENTRY 0xFFFFFFFFu

// the scene drawn as lines, mirrors the line effects of Rendering\SceneRenderer.cs.
#define LINE_NEON 1
#define LINE_NEW_YORK 2

// linear, the green of an old flight computer's display.
static const float3 NewYorkGreen = float3(0.04, 1.0, 0.17);

struct FrameConstants
{
    float4x4 viewProjection;
    float3 cameraPosition;
    float time;
    float3 lightDirection;
    uint instanceCount;
    float4 frustumPlanes[5];
    float pixelScale;
    float minimumPixels;
    float fogDensity;
    uint hoveredEntry;
    float3 cameraRight;
    uint selectedEntry;
    float3 cameraUp;
    uint showThumbnails;

    // premultiplied, transparent when the window is made of a material that shows through the sky.
    float4 skyColor;

    // the sun's view of the map, what the blocks and the ground compare their depth with, and how wide one of its texels is in the world.
    float4x4 lightViewProjection;
    float shadowTexel;
    uint showShadows;

    // when not zero the blocks only hide what is behind them and lines drawn over them carry the picture.
    uint lineEffect;
    float linePadding;
};

// the scene writes the entry under every pixel next to its color, zero where there is none, which is what picking reads back.
struct SceneOutput
{
    float4 color : SV_Target0;
    uint entry : SV_Target1;
};

// where the layout wants an entry, uploaded by the CPU, one per entry.
struct TargetInstance
{
    float3 position;
    uint color;
    float3 size;
    uint flags;
    uint parent;
    uint change;
};

// where an entry is on screen right now, owned by the GPU and eased towards the target every frame.
struct CurrentInstance
{
    float3 position;
    uint color;
    float3 size;
    uint flags;
};

// a unit box built from the vertex id alone, 6 faces of 2 triangles, wound counter clockwise seen from outside.
static const float3 FaceNormals[6] = { float3(1, 0, 0), float3(-1, 0, 0), float3(0, 1, 0), float3(0, -1, 0), float3(0, 0, 1), float3(0, 0, -1) };
static const float3 FaceU[6] = { float3(0, 0, -1), float3(0, 0, 1), float3(1, 0, 0), float3(1, 0, 0), float3(1, 0, 0), float3(-1, 0, 0) };
static const float3 FaceV[6] = { float3(0, 1, 0), float3(0, 1, 0), float3(0, 0, -1), float3(0, 0, 1), float3(0, 1, 0), float3(0, 1, 0) };
static const float2 QuadCorners[6] = { float2(-1, -1), float2(1, -1), float2(1, 1), float2(-1, -1), float2(1, 1), float2(-1, 1) };

void GetBoxVertex(uint vertexId, out float3 position, out float3 normal, out float2 uv)
{
    uint face = vertexId / 6;
    float2 corner = QuadCorners[vertexId % 6];
    normal = FaceNormals[face];

    // the box stands on its origin, so a block of any height rests on what is under it.
    position = 0.5 * (normal + corner.x * FaceU[face] + corner.y * FaceV[face]) + float3(0, 0.5, 0);
    uv = corner * 0.5 + 0.5;
}

// the two triangles of a unit quad, u to the right and v downwards.
static const float2 UnitQuadCorners[6] = { float2(0, 0), float2(1, 0), float2(1, 1), float2(0, 0), float2(1, 1), float2(0, 1) };

float FogAmount(FrameConstants frame, float3 worldPosition)
{
    return 1 - exp(-length(worldPosition - frame.cameraPosition) * frame.fogDensity);
}

// what is far away fades into the sky, in premultiplied space so a transparent sky fades it out.
float4 ApplyFog(FrameConstants frame, float3 color, float3 worldPosition)
{
    return lerp(float4(color, 1), frame.skyColor, FogAmount(frame, worldPosition));
}

// how much of the sun reaches a point, one when it is lit, zero in the shadow of a block, softened over the texels around it.
// the map stores depth reversed as the scene does, so a point is lit where it is at least as near the sun as what the map holds.
float SampleShadow(FrameConstants frame, Texture2D<float> map, SamplerComparisonState comparison, float3 worldPosition, float3 normal)
{
    if (frame.showShadows == 0)
        return 1;

    // pushed out along its normal by a texel or two, a face would otherwise shadow itself wherever the map is coarser than the face is flat.
    float3 position = worldPosition + normal * frame.shadowTexel * 1.5;
    float4 light = mul(frame.lightViewProjection, float4(position, 1));
    float2 uv = light.xy * float2(0.5, -0.5) + 0.5;
    // what lies outside the sun's window or beyond its depth range casts and receives nothing, the ground reaches far past the map.
    if (any(uv < 0) || any(uv > 1) || light.z < 0 || light.z > 1)
        return 1;

    uint width;
    uint height;
    map.GetDimensions(width, height);
    float2 texel = 1.0 / float2(width, height);
    float lit = 0;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            lit += map.SampleCmpLevelZero(comparison, uv + float2(x, y) * texel, light.z);
        }
    }
    return lit / 9;
}

float3 UnpackColor(uint color)
{
    float3 srgb = float3((color >> 16) & 0xFF, (color >> 8) & 0xFF, color & 0xFF) / 255.0;
    return srgb * srgb;
}

bool IsBoxVisible(FrameConstants frame, float3 center, float3 extents)
{
    for (uint i = 0; i < 5; i++)
    {
        float4 plane = frame.frustumPlanes[i];
        if (dot(plane.xyz, center) + plane.w + dot(abs(plane.xyz), extents) < 0)
            return false;
    }

    // smaller than a fraction of a pixel, it would only shimmer.
    float distance = max(length(center - frame.cameraPosition) - length(extents), 0.001);
    return length(extents) * 2 * frame.pixelScale / distance >= frame.minimumPixels;
}

#endif
