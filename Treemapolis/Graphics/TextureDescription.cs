namespace Treemapolis.Graphics;

public sealed class TextureDescription
{
    public uint Width { get; init; }
    public uint Height { get; init; }
    public ushort MipLevels { get; init; } = 1;
    public ushort ArraySize { get; init; } = 1;
    public DXGI_FORMAT Format { get; init; }
    public uint SampleCount { get; init; } = 1;

    // a typeless resource needs to be told what its render target view reads it as.
    public DXGI_FORMAT? RenderTargetViewFormat { get; init; }
    public DXGI_FORMAT? ShaderResourceViewFormat { get; init; }
    public bool RenderTarget { get; init; }
    public bool DepthStencil { get; init; }
    public bool ShaderResource { get; init; }
    public bool UnorderedAccess { get; init; }
    public Vector4 ClearColor { get; init; }
    public float ClearDepth { get; init; }
    public D3D12_RESOURCE_STATES InitialState { get; init; } = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON;

    // a depth buffer that is also sampled needs a typeless resource and two views of it.
    public DXGI_FORMAT ResourceFormat => DepthStencil && ShaderResource && Format == DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT ? DXGI_FORMAT.DXGI_FORMAT_R32_TYPELESS : Format;
    public DXGI_FORMAT ShaderResourceFormat => ShaderResourceViewFormat ?? (DepthStencil && Format == DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT ? DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT : Format);
    public DXGI_FORMAT RenderTargetFormat => RenderTargetViewFormat ?? Format;
    public bool IsMultisampled => SampleCount > 1;
    public DXGI_FORMAT DepthStencilFormat => Format;
}
