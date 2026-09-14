#include "Post.hlsli"

static const float3 LumaWeights = float3(0.2126, 0.7152, 0.0722);

static const float CartoonBands = 4;
static const float CartoonSaturation = 1.35;
static const float3 InkColor = float3(0.05, 0.04, 0.06);

static const float PixelCell = 6;
static const float PixelEdge = 0.82;
static const float PixelDither = 0.09;
static const float Bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

// the sixteen colors of the PICO-8 fantasy console, in sRGB.
static const float3 Palette[16] =
{
    float3(0.000, 0.000, 0.000), float3(0.114, 0.169, 0.325), float3(0.494, 0.145, 0.325), float3(0.000, 0.529, 0.318),
    float3(0.671, 0.322, 0.212), float3(0.373, 0.341, 0.310), float3(0.761, 0.765, 0.780), float3(1.000, 0.945, 0.910),
    float3(1.000, 0.000, 0.302), float3(1.000, 0.639, 0.000), float3(1.000, 0.925, 0.153), float3(0.000, 0.894, 0.212),
    float3(0.161, 0.678, 1.000), float3(0.514, 0.463, 0.612), float3(1.000, 0.467, 0.659), float3(1.000, 0.800, 0.667),
};

float3 ToGamma(float3 color)
{
    return pow(saturate(color), 1 / 2.2);
}

float3 ToLinear(float3 color)
{
    return pow(saturate(color), 2.2);
}

// the scene is premultiplied, the effects work on the color itself and premultiply again where they keep its alpha.
float3 Unpremultiply(float4 color)
{
    return color.rgb / max(color.a, 0.0001);
}

int3 Texel(float2 pixel)
{
    return int3((int2)clamp(pixel, 0, float2(width, height) - 1), 0);
}

uint EntryAt(float2 pixel)
{
    return Entries.Load(Texel(pixel));
}

// where one block meets another, looking at the pixel on the right and the one below, so a line is one pixel wide.
bool IsBlockEdge(float2 pixel)
{
    uint entry = EntryAt(pixel);
    return EntryAt(pixel + float2(1, 0)) != entry || EntryAt(pixel + float2(0, 1)) != entry;
}

// flat bands of color with a thin dark ink wherever one block meets another.
float4 Cartoon(float2 pixel)
{
    float4 source = Scene.Load(Texel(pixel));
    if (IsBlockEdge(pixel))
        return float4(ToLinear(InkColor), 1);

    float3 color = ToGamma(Unpremultiply(source));
    float luma = dot(color, LumaWeights);
    float banded = (floor(luma * CartoonBands) + 0.5) / CartoonBands;
    color *= banded / max(luma, 0.001);
    color = lerp(dot(color, LumaWeights).xxx, color, CartoonSaturation);
    return float4(ToLinear(color) * source.a, source.a);
}

// big square pixels, each the average of four samples, snapped to the nearest of sixteen colors with a light ordered dither,
// and a darker seam around each pixel.
float4 PixelArt(float2 pixel)
{
    float size = PixelCell * scale;
    float2 cell = floor(pixel / size);
    float4 source = 0;
    for (int i = 0; i < 4; i++)
    {
        float2 at = (cell + float2(0.25 + 0.5 * (i & 1), 0.25 + 0.5 * (i >> 1))) * size;
        source += Scene.Load(Texel(at)) * 0.25;
    }

    uint2 bayer = uint2(cell) % 4;
    float threshold = (Bayer[bayer.y * 4 + bayer.x] + 0.5) / 16 - 0.5;
    float3 wanted = ToGamma(Unpremultiply(source)) + threshold * PixelDither;
    float3 color = Palette[0];
    float nearest = 1e10;
    for (int index = 0; index < 16; index++)
    {
        float3 difference = (Palette[index] - wanted) * float3(0.3, 0.59, 0.11);
        float distance = dot(difference, difference);
        if (distance < nearest)
        {
            nearest = distance;
            color = Palette[index];
        }
    }

    float2 inside = pixel - cell * size;
    if (inside.x < scale || inside.y < scale)
    {
        color *= PixelEdge;
    }
    return float4(ToLinear(color) * source.a, source.a);
}

[RootSignature(PostRootSignature)]
float4 main(PostVertex input) : SV_Target
{
    float2 pixel = floor(input.position.xy);
    if (all(pixel >= islandMin) && all(pixel < islandMax))
        return Scene.Load(Texel(pixel));

    switch (effect)
    {
        case EFFECT_CARTOON:
            return Cartoon(pixel);

        case EFFECT_PIXEL_ART:
            return PixelArt(pixel);
    }
    return Scene.Load(Texel(pixel));
}
