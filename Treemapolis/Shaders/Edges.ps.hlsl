#include "Edges.hlsli"

static const float NewYorkCore = 0.8;
static const float NewYorkGlow = 0.25;
static const float NeonCore = 1.2;
static const float NeonWhite = 0.15;
static const float NeonHaloSigma = 2.5;
static const float NeonHalo = 0.35;

// a solid core with a one pixel soft edge and a glow that falls off around it, measured to the segment so both ends are round.
// the pipeline keeps the brightest light at each pixel, overlapping edges never add up into white.
[RootSignature(EdgesRootSignature)]
float4 main(EdgeVertex input) : SV_Target
{
    float beyond = max(max(-input.along, input.along - input.lengthPixels), 0);
    float pixels = length(float2(beyond, input.across));
    if (Frame.lineEffect == LINE_NEON)
    {
        float core = saturate(NeonCore * edgeScale + 0.5 - pixels);
        float sigma = NeonHaloSigma * edgeScale;
        float halo = exp(-pixels * pixels / (2 * sigma * sigma)) * NeonHalo;
        float3 neon = input.color * max(core, halo) + core * NeonWhite;
        return float4(neon * input.brightness, 0);
    }

    float stroke = saturate(NewYorkCore * edgeScale + 0.5 - pixels);
    float glow = exp(-pixels * pixels / (2 * edgeScale * edgeScale)) * NewYorkGlow;
    return float4(input.color * max(stroke, glow) * input.brightness, 0);
}
