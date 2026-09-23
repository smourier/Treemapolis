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
    }

    // borrowed for the one D3D12 call that receives it, a copy kept past that call can outlive the resource.
    public nint NativePointer => ComObject.ToComInstanceNoAddRef();
    public D3D12_RESOURCE_STATES State { get; internal set; }
    public ulong GpuVirtualAddress => NativeObject.GetGPUVirtualAddress();

    protected void SetResource(IComObject<ID3D12Resource> resource, D3D12_RESOURCE_STATES state)
    {
        ExchangeDisposable(resource);
        State = state;
    }

    // where a texture region lands in a buffer. the Windows 10 runtime refuses an offset that is not a multiple of 512
    // or a row pitch that is not a multiple of 256, a newer runtime accepts both, so both are rounded up.
    public static D3D12_PLACED_SUBRESOURCE_FOOTPRINT CreateBufferFootprint(ulong offset, DXGI_FORMAT format, uint width, uint height, uint bytesPerPixel) => new()
    {
        Offset = AlignBufferOffset(offset),
        Footprint = new D3D12_SUBRESOURCE_FOOTPRINT
        {
            Format = format,
            Width = width,
            Height = height,
            Depth = 1,
            RowPitch = (uint)AlignUp((ulong)width * bytesPerPixel, Constants.D3D12_TEXTURE_DATA_PITCH_ALIGNMENT),
        },
    };

    public static ulong AlignBufferOffset(ulong offset) => AlignUp(offset, Constants.D3D12_TEXTURE_DATA_PLACEMENT_ALIGNMENT);

    // every row of the footprint is counted at its full pitch, the last one included.
    public static ulong GetFootprintEnd(in D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint) => footprint.Offset + (ulong)footprint.Footprint.RowPitch * footprint.Footprint.Height;

    private static ulong AlignUp(ulong value, uint alignment) => (value + alignment - 1) & ~((ulong)alignment - 1);

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
