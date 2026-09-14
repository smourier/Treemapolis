namespace Treemapolis.Graphics;

public sealed class ReadbackBuffer : Resource
{
    public ReadbackBuffer(GraphicsDevice device, ulong size)
    {
        Size = size;
        SetResource(CreateBuffer(device, size, D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_READBACK, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST), D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST);
    }

    public ulong Size { get; }

    public unsafe void Read<T>(Span<T> destination, ulong offset = 0) where T : unmanaged
    {
        var bytes = MemoryMarshal.AsBytes(destination);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + (ulong)bytes.Length, Size);
        var pointer = MapCore(true);
        try
        {
            new ReadOnlySpan<byte>((byte*)pointer + offset, bytes.Length).CopyTo(bytes);
        }
        finally
        {
            UnmapCore(false);
        }
    }
}
