namespace Treemapolis.Graphics;

public sealed class CommandAllocatorPool(GraphicsDevice device, D3D12_COMMAND_LIST_TYPE type) : IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<CommandAllocator> _all = [];
    private readonly Queue<PendingAllocator> _pending = new();

    public CommandAllocator Rent(ulong completedFenceValue)
    {
        lock (_lock)
        {
            if (_pending.TryPeek(out var pending) && pending.FenceValue <= completedFenceValue)
            {
                _pending.Dequeue();
                pending.Allocator.NativeObject.Reset().ThrowOnError();
                return pending.Allocator;
            }

            var allocator = new CommandAllocator(device.ComObject.CreateCommandAllocator(type));
            _all.Add(allocator);
            return allocator;
        }
    }

    // an allocator can only be reset once the GPU is done with every list recorded into it.
    public void Return(ulong fenceValue, CommandAllocator allocator)
    {
        ArgumentNullException.ThrowIfNull(allocator);
        lock (_lock)
        {
            _pending.Enqueue(new PendingAllocator(fenceValue, allocator));
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var allocator in _all)
            {
                allocator.Dispose();
            }

            _all.Clear();
            _pending.Clear();
        }
    }

    private readonly record struct PendingAllocator(ulong FenceValue, CommandAllocator Allocator);
}
