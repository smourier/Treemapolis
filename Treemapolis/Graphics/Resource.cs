namespace Treemapolis.Graphics;

public abstract class Resource : InterlockedComObject<ID3D12Resource>
{
    protected Resource()
    {
    }

    protected Resource(IComObject<ID3D12Resource> resource, D3D12_RESOURCE_STATES state)
        : base(resource)
    {
        State = state;
        NativePointer = DirectN.Extensions.Com.ComObject.ToComInstanceOfTypeNoAddRef<ID3D12Resource>(resource.Object);
    }

    // borrowed for barriers and copy locations, valid for as long as the resource lives.
    public nint NativePointer { get; private set; }
    public D3D12_RESOURCE_STATES State { get; internal set; }
    public ulong GpuVirtualAddress => NativeObject.GetGPUVirtualAddress();

    protected void SetResource(IComObject<ID3D12Resource> resource, D3D12_RESOURCE_STATES state)
    {
        ExchangeDisposable(resource);
        State = state;
        NativePointer = DirectN.Extensions.Com.ComObject.ToComInstanceOfTypeNoAddRef<ID3D12Resource>(resource.Object);
    }

    protected static IComObject<ID3D12Resource> CreateBuffer(GraphicsDevice device, ulong size, D3D12_HEAP_TYPE heapType, D3D12_RESOURCE_FLAGS flags, D3D12_RESOURCE_STATES state)
    {
        ArgumentNullException.ThrowIfNull(device);
        var props = new D3D12_HEAP_PROPERTIES { Type = heapType };
        var desc = new D3D12_RESOURCE_DESC
        {
            Dimension = D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_BUFFER,
            Width = Math.Max(1, size),
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Layout = D3D12_TEXTURE_LAYOUT.D3D12_TEXTURE_LAYOUT_ROW_MAJOR,
            Flags = flags,
        };
        return device.ComObject.CreateCommittedResource(props, D3D12_HEAP_FLAGS.D3D12_HEAP_FLAG_NONE, desc, state);
    }

    protected unsafe nint MapCore(bool read)
    {
        nint pointer;
        var noRead = new D3D12_RANGE();
        NativeObject.Map(0, read ? 0 : (nint)(&noRead), (nint)(&pointer)).ThrowOnError();
        return pointer;
    }

    protected unsafe void UnmapCore(bool wrote)
    {
        var noWrite = new D3D12_RANGE();
        NativeObject.Unmap(0, wrote ? 0 : (nint)(&noWrite));
    }
}
