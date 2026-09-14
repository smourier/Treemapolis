// an entry is an index, averaging samples would invent one, so picking reads the first sample of every pixel.
Texture2DMS<uint> Source : register(t0);
RWTexture2D<uint> Destination : register(u0);

[RootSignature("RootFlags(0), DescriptorTable(SRV(t0)), DescriptorTable(UAV(u0))")]
[numthreads(16, 16, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint width;
    uint height;
    uint samples;
    Source.GetDimensions(width, height, samples);
    if (id.x >= width || id.y >= height)
        return;

    Destination[id.xy] = Source.Load(id.xy, 0);
}
