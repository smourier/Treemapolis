namespace Treemapolis.Graphics;

public sealed class BackBuffer : Resource
{
    private readonly DescriptorHeap _heap;

    public BackBuffer(GraphicsDevice device, DescriptorHeap heap, IComObject<ID3D12Resource> resource, DXGI_FORMAT renderTargetFormat)
        : base(resource, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(heap);
        _heap = heap;
        RenderTargetView = heap.Allocate();
        var rtv = new D3D12_RENDER_TARGET_VIEW_DESC { Format = renderTargetFormat, ViewDimension = D3D12_RTV_DIMENSION.D3D12_RTV_DIMENSION_TEXTURE2D };
        device.ComObject.CreateRenderTargetView(resource, rtv, RenderTargetView.Cpu);
    }

    public DescriptorHandle RenderTargetView { get; }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            _heap.Free(RenderTargetView);
        }
        base.Dispose(disposing);
    }
}
