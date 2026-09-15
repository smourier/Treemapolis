namespace Treemapolis.Rendering;

// what is under a point is read from the entry buffer the scene already wrote, one texel per request,
// copied into the readback of the frame slot and read when that slot comes around again, so it never stalls the GPU.
public sealed class PickBuffer : IDisposable
{
    private const int _maxPerFrame = 8;
    private const uint _texelSize = sizeof(uint);

    private readonly D3D12_PLACED_SUBRESOURCE_FOOTPRINT[] _texelFootprints = new D3D12_PLACED_SUBRESOURCE_FOOTPRINT[_maxPerFrame];
    private readonly ReadbackBuffer[] _readbacks;
    private readonly PickRequest[][] _recorded;
    private readonly bool[][] _recordedInside;
    private readonly int[] _recordedCounts;
    private readonly List<PickRequest> _queued = [];
    private PickRequest? _hover;

    public PickBuffer(GraphicsDevice device, uint frameSlots)
    {
        ArgumentNullException.ThrowIfNull(device);
        var end = 0ul;
        for (var i = 0; i < _maxPerFrame; i++)
        {
            _texelFootprints[i] = Resource.CreateBufferFootprint(end, DXGI_FORMAT.DXGI_FORMAT_R32_UINT, 1, 1, _texelSize);
            end = Resource.GetFootprintEnd(_texelFootprints[i]);
        }

        _readbacks = new ReadbackBuffer[frameSlots];
        _recorded = new PickRequest[frameSlots][];
        _recordedInside = new bool[frameSlots][];
        _recordedCounts = new int[frameSlots];
        for (var i = 0; i < frameSlots; i++)
        {
            _readbacks[i] = new ReadbackBuffer(device, end);
            _recorded[i] = new PickRequest[_maxPerFrame];
            _recordedInside[i] = new bool[_maxPerFrame];
        }
    }

    public bool HasRequests => _queued.Count > 0 || _hover != null;

    public bool IsPending
    {
        get
        {
            if (_queued.Count > 0 || _hover != null)
                return true;

            foreach (var count in _recordedCounts)
            {
                if (count > 0)
                    return true;
            }
            return false;
        }
    }

    public void Request(PickRequest request)
    {
        if (request.Action == PickAction.Hover)
        {
            _hover = request;
            return;
        }

        if (_queued.Count < _maxPerFrame)
        {
            _queued.Add(request);
        }
    }

    public void Record(CommandList list, uint slot, Texture entries)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(entries);
        if (_hover != null)
        {
            _queued.Add(_hover.Value);
            _hover = null;
        }

        var count = 0;
        var readback = _readbacks[slot];
        foreach (var request in _queued)
        {
            if (count == _maxPerFrame)
                break;

            var inside = request.X >= 0 && request.Y >= 0 && request.X < entries.Width && request.Y < entries.Height;
            _recordedInside[slot][count] = inside;
            if (!inside)
            {
                _recorded[slot][count++] = request;
                continue;
            }

            list.Transition(entries, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE);
            list.CopyTexelToBuffer(entries, readback, (uint)request.X, (uint)request.Y, _texelFootprints[count]);
            _recorded[slot][count++] = request;
        }

        _queued.Clear();
        _recordedCounts[slot] = count;
    }

    public void Collect(uint slot, List<PickResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var count = _recordedCounts[slot];
        if (count == 0)
            return;

        Span<uint> texel = stackalloc uint[1];
        for (var i = 0; i < count; i++)
        {
            var request = _recorded[slot][i];
            texel[0] = 0;
            if (_recordedInside[slot][i])
            {
                _readbacks[slot].Read(texel, _texelFootprints[i].Offset);
            }

            var entry = texel[0] == 0 ? Entry.None : (int)texel[0] - 1;
            results.Add(new PickResult(request, entry));
        }
        _recordedCounts[slot] = 0;
    }

    public void Dispose()
    {
        foreach (var readback in _readbacks)
        {
            readback.Dispose();
        }
    }
}
