namespace Treemapolis.Graphics;

public sealed class CommandQueue : InterlockedComObject<ID3D12CommandQueue>
{
    private readonly Lock _fenceLock = new();
    private readonly Lock _eventLock = new();
    private readonly IComObject<ID3D12Fence> _fence;
    private readonly AutoResetEvent _fenceEvent = new(false);
    private readonly CommandAllocatorPool _allocators;
    private ulong _nextFenceValue;
    private ulong _lastCompletedFenceValue;

    public CommandQueue(GraphicsDevice device, D3D12_COMMAND_LIST_TYPE type)
    {
        ArgumentNullException.ThrowIfNull(device);
        Device = device;
        Type = type;
        _allocators = new CommandAllocatorPool(device, type);

        var desc = new D3D12_COMMAND_QUEUE_DESC { Type = type };
        ExchangeDisposable(device.ComObject.CreateCommandQueue(desc));
        _fence = device.ComObject.CreateFence(0);

        // the list type in the top byte keeps fence values of different queues apart.
        _nextFenceValue = (ulong)type << 56 | 1;
        _lastCompletedFenceValue = (ulong)type << 56;
        _fence.Signal(_lastCompletedFenceValue);
        TimestampFrequency = ComObject.GetTimestampFrequency();
    }

    public GraphicsDevice Device { get; }
    public D3D12_COMMAND_LIST_TYPE Type { get; }
    public ulong TimestampFrequency { get; }

    public override string ToString() => Type.ToString();

    internal CommandAllocator RentAllocator() => _allocators.Rent(_fence.GetCompletedValue());
    internal void ReturnAllocator(ulong fenceValue, CommandAllocator allocator) => _allocators.Return(fenceValue, allocator);

    public ulong Execute(CommandList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        lock (_fenceLock)
        {
            NativeObject.ExecuteCommandLists(1, [list.NativeObject]);
            NativeObject.Signal(_fence.Object, _nextFenceValue).ThrowOnError();
            return _nextFenceValue++;
        }
    }

    public ulong SignalAndIncrementFence()
    {
        lock (_fenceLock)
        {
            NativeObject.Signal(_fence.Object, _nextFenceValue).ThrowOnError();
            return _nextFenceValue++;
        }
    }

    public bool IsFenceComplete(ulong fenceValue)
    {
        if (fenceValue > _lastCompletedFenceValue)
        {
            _lastCompletedFenceValue = Math.Max(_lastCompletedFenceValue, _fence.GetCompletedValue());
        }
        return fenceValue <= _lastCompletedFenceValue;
    }

    public void WaitForFence(ulong fenceValue)
    {
        if (IsFenceComplete(fenceValue))
            return;

        // a managed wait on an STA thread pumps messages, and a resize dispatched in the middle of it would release buffers still in flight.
        lock (_eventLock)
        {
            var handle = new HANDLE { Value = _fenceEvent.SafeWaitHandle.DangerousGetHandle() };
            _fence.SetEventOnCompletion(fenceValue, handle);
            Functions.WaitForSingleObject(handle, uint.MaxValue);
            _lastCompletedFenceValue = Math.Max(_lastCompletedFenceValue, fenceValue);
        }
    }

    public void WaitForIdle() => WaitForFence(SignalAndIncrementFence());

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _allocators.Dispose();
            _fence.Dispose();
            _fenceEvent.Dispose();
        }
        base.Dispose(disposing);
    }
}
