namespace Treemapolis.Graphics;

// per pass GPU time from timestamp queries. every frame slot owns its queries and readback,
// and a slot is only read when the swap chain hands it out again, by which time the GPU has finished it.
public sealed class GpuTimer : IDisposable
{
    private const int _maxScopes = 32;
    private readonly IComObject<ID3D12QueryHeap> _heap;
    private readonly ReadbackBuffer[] _readbacks;
    private readonly bool[] _pending;
    private readonly ulong _frequency;
    private readonly List<string> _names = [];
    private readonly double[] _milliseconds = new double[_maxScopes];
    private readonly ulong[] _values = new ulong[_maxScopes * 2];
    private uint _frameSlot;

    public GpuTimer(GraphicsDevice device, uint frameSlots)
    {
        ArgumentNullException.ThrowIfNull(device);
        _frequency = device.DirectQueue.TimestampFrequency;
        var desc = new D3D12_QUERY_HEAP_DESC
        {
            Type = D3D12_QUERY_HEAP_TYPE.D3D12_QUERY_HEAP_TYPE_TIMESTAMP,
            Count = frameSlots * _maxScopes * 2,
        };
        device.NativeObject.CreateQueryHeap(desc, typeof(ID3D12QueryHeap).GUID, out var heap).ThrowOnError();
        _heap = DirectN.Extensions.Com.ComObject.FromPointer<ID3D12QueryHeap>(heap)!;

        _readbacks = new ReadbackBuffer[frameSlots];
        _pending = new bool[frameSlots];
        for (var i = 0; i < frameSlots; i++)
        {
            _readbacks[i] = new ReadbackBuffer(device, _maxScopes * 2 * sizeof(ulong));
        }
    }

    public IReadOnlyList<string> Names => _names;

    public int Register(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_names.Count == _maxScopes)
            throw new InvalidOperationException();

        _names.Add(name);
        return _names.Count - 1;
    }

    public double GetMilliseconds(int scope) => _milliseconds[scope];

    public void BeginFrame(uint frameSlot)
    {
        _frameSlot = frameSlot;
        if (!_pending[frameSlot])
            return;

        var values = _values.AsSpan(0, _names.Count * 2);
        _readbacks[frameSlot].Read(values);
        for (var i = 0; i < _names.Count; i++)
        {
            var start = values[i * 2];
            var end = values[i * 2 + 1];
            _milliseconds[i] = end > start ? (end - start) * 1000.0 / _frequency : 0;
        }
        _pending[frameSlot] = false;
    }

    public void Begin(CommandList list, int scope) => list.NativeObject.EndQuery(_heap.Object, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, QueryIndex(scope, 0));
    public void End(CommandList list, int scope) => list.NativeObject.EndQuery(_heap.Object, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, QueryIndex(scope, 1));

    public void EndFrame(CommandList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (_names.Count == 0)
            return;

        list.NativeObject.ResolveQueryData(_heap.Object, D3D12_QUERY_TYPE.D3D12_QUERY_TYPE_TIMESTAMP, QueryIndex(0, 0), (uint)_names.Count * 2, _readbacks[_frameSlot].NativeObject, 0);
        _pending[_frameSlot] = true;
    }

    private uint QueryIndex(int scope, int edge) => _frameSlot * _maxScopes * 2 + (uint)(scope * 2 + edge);

    public void Dispose()
    {
        foreach (var readback in _readbacks)
        {
            readback.Dispose();
        }
        _heap.Dispose();
    }
}
