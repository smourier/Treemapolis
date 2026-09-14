namespace Treemapolis.Graphics;

// one heap per type for the whole device. a shader visible heap must be the single one bound to a command list,
// so descriptors are never spread across several heaps.
public sealed class DescriptorHeap : InterlockedComObject<ID3D12DescriptorHeap>
{
    private readonly Lock _lock = new();
    private readonly Stack<uint> _free = new();
    private readonly nuint _cpuStart;
    private readonly ulong _gpuStart;
    private uint _next;

    public DescriptorHeap(GraphicsDevice device, D3D12_DESCRIPTOR_HEAP_TYPE type, uint capacity, bool shaderVisible)
    {
        ArgumentNullException.ThrowIfNull(device);
        Type = type;
        Capacity = capacity;
        var desc = new D3D12_DESCRIPTOR_HEAP_DESC
        {
            Type = type,
            NumDescriptors = capacity,
            Flags = shaderVisible ? D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE : D3D12_DESCRIPTOR_HEAP_FLAGS.D3D12_DESCRIPTOR_HEAP_FLAG_NONE,
        };

        ExchangeDisposable(device.ComObject.CreateDescriptorHeap(desc));
        IncrementSize = device.ComObject.GetDescriptorHandleIncrementSize(type);
        _cpuStart = ComObject.GetCPUDescriptorHandleForHeapStart().ptr;
        if (shaderVisible)
        {
            _gpuStart = ComObject.GetGPUDescriptorHandleForHeapStart().ptr;
        }
    }

    public D3D12_DESCRIPTOR_HEAP_TYPE Type { get; }
    public uint Capacity { get; }
    public uint IncrementSize { get; }
    public uint Count { get { lock (_lock) { return _next - (uint)_free.Count; } } }

    public DescriptorHandle Allocate()
    {
        uint index;
        lock (_lock)
        {
            if (!_free.TryPop(out index))
            {
                if (_next == Capacity)
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, Res.DescriptorHeapFull, Type, Capacity));

                index = _next++;
            }
        }
        return GetHandle(index);
    }

    public DescriptorHandle GetHandle(uint index) => new(
        index,
        new D3D12_CPU_DESCRIPTOR_HANDLE { ptr = _cpuStart + index * IncrementSize },
        new D3D12_GPU_DESCRIPTOR_HANDLE { ptr = _gpuStart == 0 ? 0 : _gpuStart + index * IncrementSize });

    public void Free(DescriptorHandle handle)
    {
        if (!handle.IsValid || IsDisposed)
            return;

        lock (_lock)
        {
            _free.Push(handle.Index);
        }
    }
}
