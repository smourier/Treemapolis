namespace Treemapolis.Graphics;

public sealed class DescriptorHeaps : IDisposable
{
    private const uint _renderTargetCapacity = 64;
    private const uint _depthStencilCapacity = 16;
    private const uint _shaderResourceCapacity = 16384;

    public DescriptorHeaps(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        RenderTargets = new DescriptorHeap(device, D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_RTV, _renderTargetCapacity, false);
        DepthStencils = new DescriptorHeap(device, D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_DSV, _depthStencilCapacity, false);
        ShaderResources = new DescriptorHeap(device, D3D12_DESCRIPTOR_HEAP_TYPE.D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV, _shaderResourceCapacity, true);
    }

    public DescriptorHeap RenderTargets { get; }
    public DescriptorHeap DepthStencils { get; }
    public DescriptorHeap ShaderResources { get; }

    public void Dispose()
    {
        RenderTargets.Dispose();
        DepthStencils.Dispose();
        ShaderResources.Dispose();
    }
}
