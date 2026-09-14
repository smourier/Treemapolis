namespace Treemapolis.Graphics;

// stays mapped for its whole life, an upload heap allows it and it saves a map per write.
public sealed class UploadBuffer : Resource
{
    public UploadBuffer(GraphicsDevice device, ulong size)
    {
        Size = size;
        SetResource(CreateBuffer(device, size, D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_NONE, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ), D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ);
        Pointer = MapCore(false);
    }

    public ulong Size { get; }
    public nint Pointer { get; }

    public unsafe void Write<T>(ReadOnlySpan<T> items, ulong offset = 0) where T : unmanaged
    {
        var bytes = MemoryMarshal.AsBytes(items);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + (ulong)bytes.Length, Size);
        bytes.CopyTo(new Span<byte>((byte*)Pointer + offset, bytes.Length));
    }

    public unsafe void Write<T>(in T item, ulong offset = 0) where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + (ulong)sizeof(T), Size);
        Unsafe.WriteUnaligned((byte*)Pointer + offset, item);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            UnmapCore(true);
        }
        base.Dispose(disposing);
    }
}
