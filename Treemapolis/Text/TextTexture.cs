namespace Treemapolis.Text;

// an R8 coverage texture with a CPU mirror. what changed is uploaded as one band of rows through a buffer per frame slot,
// so a band never overwrites a buffer the GPU is still copying from.
public sealed class TextTexture : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly UploadBuffer[] _uploads;
    private readonly D3D12_PLACED_SUBRESOURCE_FOOTPRINT _rowFootprint;
    private int _dirtyTop = int.MaxValue;
    private int _dirtyBottom;

    public unsafe TextTexture(GraphicsDevice device, DescriptorHeaps heaps, uint width, uint height, uint frameSlots)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(heaps);
        _device = device;
        Width = width;
        Height = height;
        Pixels = new byte[width * height];
        Texture = new Texture(device, heaps, new TextureDescription
        {
            Width = width,
            Height = height,
            Format = DXGI_FORMAT.DXGI_FORMAT_R8_UNORM,
            ShaderResource = true,
        });

        var desc = new D3D12_RESOURCE_DESC
        {
            Dimension = D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_TEXTURE2D,
            Width = width,
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_R8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
        };

        D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint;
        device.NativeObject.GetCopyableFootprints(desc, 0, 1, 0, (nint)(&footprint), 0, 0, 0);
        _rowFootprint = footprint;

        _uploads = new UploadBuffer[frameSlots];
        for (var i = 0; i < frameSlots; i++)
        {
            _uploads[i] = new UploadBuffer(device, (ulong)_rowFootprint.Footprint.RowPitch * height);
        }
    }

    public uint Width { get; }
    public uint Height { get; }
    public byte[] Pixels { get; }
    public Texture Texture { get; }

    public void MarkDirty(int top, int bottom)
    {
        _dirtyTop = Math.Min(_dirtyTop, top);
        _dirtyBottom = Math.Max(_dirtyBottom, bottom);
    }

    public unsafe void Upload(CommandList list, uint frameSlot)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (_dirtyTop >= _dirtyBottom)
            return;

        var top = Math.Clamp(_dirtyTop, 0, (int)Height);
        var bottom = Math.Clamp(_dirtyBottom, top, (int)Height);
        _dirtyTop = int.MaxValue;
        _dirtyBottom = 0;
        if (bottom == top)
            return;

        var rows = bottom - top;
        var pitch = (int)_rowFootprint.Footprint.RowPitch;
        var upload = _uploads[frameSlot];
        var target = new Span<byte>((void*)upload.Pointer, pitch * rows);
        for (var y = 0; y < rows; y++)
        {
            Pixels.AsSpan((top + y) * (int)Width, (int)Width).CopyTo(target[(y * pitch)..]);
        }

        var source = new D3D12_TEXTURE_COPY_LOCATION { pResource = upload.NativePointer, Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
        var footprint = _rowFootprint;
        footprint.Footprint.Height = (uint)rows;
        source.Anonymous.PlacedFootprint = footprint;
        var destination = new D3D12_TEXTURE_COPY_LOCATION { pResource = Texture.NativePointer, Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };

        list.Transition(Texture, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST);
        list.NativeObject.CopyTextureRegion(destination, 0, (uint)top, 0, source, 0);
        list.Transition(Texture, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);
    }

    public void Dispose()
    {
        foreach (var upload in _uploads)
        {
            upload.Dispose();
        }
        Texture.Dispose();
    }
}
