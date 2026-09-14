namespace Treemapolis.Graphics;

public sealed class CommandList : InterlockedComObject<ID3D12GraphicsCommandList>
{
    private readonly CommandQueue _queue;
    private readonly ID3D12CommandList[] _executeArray;
    private readonly D3D12_RESOURCE_BARRIER[] _barrier = new D3D12_RESOURCE_BARRIER[1];
    private readonly ID3D12DescriptorHeap[] _heapArray = new ID3D12DescriptorHeap[1];
    private CommandAllocator? _allocator;

    public CommandList(CommandQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _queue = queue;
        _allocator = queue.RentAllocator();
        ExchangeDisposable(queue.Device.ComObject.CreateCommandList<ID3D12GraphicsCommandList>(0, queue.Type, _allocator.ComObject));
        NativeObject.Close().ThrowOnError();
        _executeArray = [NativeObject];
    }

    internal ID3D12CommandList[] ExecuteArray => _executeArray;

    public void Begin()
    {
        _allocator ??= _queue.RentAllocator();
        NativeObject.Reset(_allocator.NativeObject, null).ThrowOnError();
    }

    public ulong Execute()
    {
        NativeObject.Close().ThrowOnError();
        var fence = _queue.Execute(this);
        if (_allocator != null)
        {
            _queue.ReturnAllocator(fence, _allocator);
            _allocator = null;
        }
        return fence;
    }

    public void Transition(Resource resource, D3D12_RESOURCE_STATES state)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (resource.State == state)
            return;

        _barrier[0] = new D3D12_RESOURCE_BARRIER { Type = D3D12_RESOURCE_BARRIER_TYPE.D3D12_RESOURCE_BARRIER_TYPE_TRANSITION };
        _barrier[0].Anonymous.Transition = new D3D12_RESOURCE_TRANSITION_BARRIER
        {
            pResource = resource.NativePointer,
            StateBefore = resource.State,
            StateAfter = state,
            Subresource = Constants.D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES,
        };
        NativeObject.ResourceBarrier(1, _barrier);
        resource.State = state;
    }

    public void UnorderedAccessBarrier(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _barrier[0] = new D3D12_RESOURCE_BARRIER { Type = D3D12_RESOURCE_BARRIER_TYPE.D3D12_RESOURCE_BARRIER_TYPE_UAV };
        _barrier[0].Anonymous.UAV = new D3D12_RESOURCE_UAV_BARRIER { pResource = resource.NativePointer };
        NativeObject.ResourceBarrier(1, _barrier);
    }

    public void SetDescriptorHeap(DescriptorHeap heap)
    {
        ArgumentNullException.ThrowIfNull(heap);
        _heapArray[0] = heap.NativeObject;
        NativeObject.SetDescriptorHeaps(1, _heapArray);
    }

    public unsafe void SetRenderTargets(ReadOnlySpan<D3D12_CPU_DESCRIPTOR_HANDLE> renderTargets, DescriptorHandle depthStencil)
    {
        var dsv = depthStencil.Cpu;
        fixed (D3D12_CPU_DESCRIPTOR_HANDLE* targets = renderTargets)
        {
            NativeObject.OMSetRenderTargets((uint)renderTargets.Length, (nint)targets, false, depthStencil.IsValid ? (nint)(&dsv) : 0);
        }
    }

    public void SetViewport(uint width, uint height)
    {
        NativeObject.RSSetViewports(1, [new D3D12_VIEWPORT { Width = width, Height = height, MaxDepth = 1 }]);
        NativeObject.RSSetScissorRects(1, [new RECT(0, 0, (int)width, (int)height)]);
    }

    public void ClearRenderTarget(DescriptorHandle view, Vector4 color) => NativeObject.ClearRenderTargetView(view.Cpu, [color.X, color.Y, color.Z, color.W], 0, 0);
    public void ClearDepth(DescriptorHandle view, float depth) => NativeObject.ClearDepthStencilView(view.Cpu, D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH, depth, 0, 0, 0);

    public unsafe void CopyTextureToBuffer(Resource source, ReadbackBuffer destination, in D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        var dst = new D3D12_TEXTURE_COPY_LOCATION { pResource = destination.NativePointer, Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
        dst.Anonymous.PlacedFootprint = footprint;
        var src = new D3D12_TEXTURE_COPY_LOCATION { pResource = source.NativePointer, Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
        NativeObject.CopyTextureRegion(dst, 0, 0, 0, src, 0);
    }

    public unsafe void CopyTexelToBuffer(Resource source, ReadbackBuffer destination, uint x, uint y, ulong offset, DXGI_FORMAT format, uint texelSize)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        var dst = new D3D12_TEXTURE_COPY_LOCATION { pResource = destination.NativePointer, Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
        dst.Anonymous.PlacedFootprint = new D3D12_PLACED_SUBRESOURCE_FOOTPRINT
        {
            Offset = offset,
            Footprint = new D3D12_SUBRESOURCE_FOOTPRINT { Format = format, Width = 1, Height = 1, Depth = 1, RowPitch = texelSize },
        };
        var src = new D3D12_TEXTURE_COPY_LOCATION { pResource = source.NativePointer, Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
        var box = new D3D12_BOX { left = x, top = y, front = 0, right = x + 1, bottom = y + 1, back = 1 };
        NativeObject.CopyTextureRegion(dst, 0, 0, 0, src, (nint)(&box));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _allocator != null)
        {
            _queue.ReturnAllocator(0, _allocator);
            _allocator = null;
        }
        base.Dispose(disposing);
    }
}
