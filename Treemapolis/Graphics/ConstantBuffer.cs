namespace Treemapolis.Graphics;

// one slot per frame in flight, so the CPU never rewrites values the GPU is still reading.
public sealed class ConstantBuffer<T> : IDisposable where T : unmanaged
{
    private const uint _alignment = 256;
    private readonly UploadBuffer _buffer;

    public unsafe ConstantBuffer(GraphicsDevice device, uint slotCount)
    {
        ArgumentOutOfRangeException.ThrowIfZero(slotCount);
        SlotCount = slotCount;
        SlotSize = ((uint)sizeof(T) + _alignment - 1) & ~(_alignment - 1);
        _buffer = new UploadBuffer(device, (ulong)SlotSize * slotCount);
    }

    public uint SlotCount { get; }
    public uint SlotSize { get; }

    public void Write(uint slot, in T value) => _buffer.Write(value, (ulong)SlotSize * slot);
    public ulong GetGpuVirtualAddress(uint slot) => _buffer.GpuVirtualAddress + (ulong)SlotSize * slot;

    public void Dispose() => _buffer.Dispose();
}
