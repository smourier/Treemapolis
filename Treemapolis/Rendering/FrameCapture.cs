namespace Treemapolis.Rendering;

// copies the back buffer before it is presented, since a flip model swap chain leaves it undefined afterwards.
public sealed class FrameCapture : IDisposable
{
    private readonly ReadbackBuffer _readback;
    private readonly D3D12_PLACED_SUBRESOURCE_FOOTPRINT _footprint;

    public unsafe FrameCapture(GraphicsDevice device, SwapChain swapChain, string filePath)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(swapChain);
        ArgumentNullException.ThrowIfNull(filePath);
        FilePath = filePath;
        Width = swapChain.Width;
        Height = swapChain.Height;

        var desc = new D3D12_RESOURCE_DESC
        {
            Dimension = D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_TEXTURE2D,
            Width = Width,
            Height = Height,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = swapChain.Format,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
        };

        D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint;
        ulong total;
        device.NativeObject.GetCopyableFootprints(desc, 0, 1, 0, (nint)(&footprint), 0, 0, (nint)(&total));
        _footprint = footprint;
        _readback = new ReadbackBuffer(device, total);
    }

    public string FilePath { get; }
    public uint Width { get; }
    public uint Height { get; }

    public void Record(CommandList list, SwapChain swapChain)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(swapChain);
        list.CopyTextureToBuffer(swapChain.CurrentBuffer, _readback, _footprint);
    }

    // the chrome, premultiplied BGRA rows the size of the frame, is laid over the scene the way composition lays it.
    public void Save(byte[]? chrome)
    {
        var stride = _footprint.Footprint.RowPitch;
        var pixels = new byte[stride * Height];

        // the row pitch is aligned but the last row is not, so the buffer holds less than a full stride times the height.
        _readback.Read(pixels.AsSpan(0, (int)(_readback.Size - _footprint.Offset)), _footprint.Offset);
        if (chrome != null && chrome.Length >= Width * Height * 4)
        {
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var source = (int)((y * Width + x) * 4);
                    var target = (int)(y * stride + x * 4);
                    var inverse = 255 - chrome[source + 3];
                    pixels[target] = (byte)(chrome[source + 2] + pixels[target] * inverse / 255);
                    pixels[target + 1] = (byte)(chrome[source + 1] + pixels[target + 1] * inverse / 255);
                    pixels[target + 2] = (byte)(chrome[source] + pixels[target + 2] * inverse / 255);
                }
            }
        }
        using var bitmap = WicBitmapSource.FromMemory(Width, Height, WicPixelFormat.GUID_WICPixelFormat32bppRGB, stride, pixels);
        bitmap.Save(FilePath);
    }

    public void Dispose() => _readback.Dispose();
}
