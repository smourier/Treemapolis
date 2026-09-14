#include "Blocks.hlsli"

static const float RimRatio = 0.03;
static const float MinimumRim = 0.004;
static const float MaximumRim = 1.2;
static const float3 AddedColor = float3(0.3, 1.0, 0.45);
static const float3 ChangedColor = float3(1.0, 0.72, 0.2);
static const float3 RemovedColor = float3(1.0, 0.25, 0.2);
static const float FlashRate = 10;
static const float BounceLift = 0.35;
static const float BounceRate = 9;
static const float BounceDamping = 3;
static const float PI = 3.14159265;

[RootSignature(BlocksRootSignature)]
BlockVertex main(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
{
    uint index = Visible[instanceId];
    CurrentInstance instance = Current[index];

    float3 local;
    float3 normal;
    float2 uv;
    GetBoxVertex(vertexId, local, normal, uv);
    uint face = vertexId / 6;

    // a change flashes and fades over a few seconds, what appeared or changed bounces up first, what was removed only glows while it sinks.
    float4 change = 0;
    float bounce = 0;
    uint stamp = Targets[index].change;
    uint kind = stamp >> CHANGE_KIND_SHIFT;
    if (kind != 0)
    {
        uint now = (uint)(Frame.time * 1000) & CHANGE_TIME_MASK;
        float age = ((now - (stamp & CHANGE_TIME_MASK)) & CHANGE_TIME_MASK) / 1000.0;
        if (age < CHANGE_SECONDS)
        {
            float fade = 1 - age / CHANGE_SECONDS;
            change.rgb = kind == CHANGE_ADDED ? AddedColor : kind == CHANGE_CHANGED ? ChangedColor : RemovedColor;
            change.a = fade * fade * (0.65 + 0.35 * cos(age * FlashRate));
            // a file jumps up off its folder and settles back, a folder stays put under what it holds.
            if (kind != CHANGE_REMOVED && (instance.flags & INSTANCE_CONTAINER) == 0)
            {
                bounce = BounceLift * min(instance.size.x, instance.size.z) * exp(-age * BounceDamping) * abs(sin(age * BounceRate));
            }
        }
    }

    BlockVertex output;
    output.change = change;
    output.worldPosition = instance.position + float3(0, bounce, 0) + local * instance.size;
    output.position = mul(Frame.viewProjection, float4(output.worldPosition, 1));
    output.normal = normal;
    output.uv = uv;

    // the edges are measured in world units, a fraction of a face would be a thick band on a wide slab and nothing on a thin side.
    output.faceSize = float2(dot(abs(FaceU[face]), instance.size), dot(abs(FaceV[face]), instance.size));
    output.rimWidth = clamp(min(instance.size.x, instance.size.z) * RimRatio, MinimumRim, MaximumRim);
    output.color = UnpackColor(instance.color);
    output.flags = instance.flags;
    output.entry = index;
    return output;
}
