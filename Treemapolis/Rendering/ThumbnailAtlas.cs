namespace Treemapolis.Rendering;

// thumbnails for the blocks of files: square cells in the slices of a texture array, each cell with its own mip levels,
// and a per entry lookup the block shader reads to find the cell of the block it draws. the least recently seen cell makes room.
public sealed class ThumbnailAtlas : IDisposable
{
    public const int CellsPerRow = 16;
    public const int Slices = 4;
    public const int CellsPerSlice = CellsPerRow * CellsPerRow;
    public const int CellCount = CellsPerSlice * Slices;

    private const int _maximumUploadsPerFrame = 8;
    private const int _bytesPerPixel = 4;
    private const uint _minimumCapacity = 64 * 1024;
    private const DXGI_FORMAT _format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;

    private readonly GraphicsDevice _device;
    private readonly UploadBuffer[] _uploads;
    private readonly int[] _cellEntries = new int[CellCount];
    private readonly long[] _cellSeen = new long[CellCount];
    private readonly Dictionary<int, int> _entryCells = [];
    private readonly Queue<Thumbnail> _pending = new();
    private readonly D3D12_PLACED_SUBRESOURCE_FOOTPRINT[] _levelFootprints = new D3D12_PLACED_SUBRESOURCE_FOOTPRINT[ThumbnailLoader.LevelCount];
    private readonly ulong _bytesPerThumbnail;
    private NamespaceTree? _tree;
    private long _frame;

    public ThumbnailAtlas(GraphicsDevice device, DescriptorHeaps heaps, uint frameSlots)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(heaps);
        _device = device;
        Texture = new Texture(device, heaps, new TextureDescription
        {
            Width = ThumbnailLoader.CellSize * CellsPerRow,
            Height = ThumbnailLoader.CellSize * CellsPerRow,
            ArraySize = Slices,
            MipLevels = ThumbnailLoader.LevelCount,
            Format = _format,
            ShaderResource = true,
        });

        var end = 0ul;
        for (var level = 0; level < ThumbnailLoader.LevelCount; level++)
        {
            var side = (uint)(ThumbnailLoader.CellSize >> level);
            _levelFootprints[level] = Resource.CreateBufferFootprint(end, _format, side, side, _bytesPerPixel);
            end = Resource.GetFootprintEnd(_levelFootprints[level]);
        }
        _bytesPerThumbnail = Resource.AlignBufferOffset(end);

        _uploads = new UploadBuffer[frameSlots];
        for (var i = 0; i < frameSlots; i++)
        {
            _uploads[i] = new UploadBuffer(device, _bytesPerThumbnail * _maximumUploadsPerFrame);
        }

        CellExtents = new UploadBuffer(device, (ulong)(CellCount * Unsafe.SizeOf<Vector2>()));
        EntryCells = new UploadBuffer(device, _minimumCapacity * sizeof(uint));
        Array.Fill(_cellEntries, Entry.None);
    }

    public Texture Texture { get; }

    // the part of its cell a picture covers, as fractions of the cell.
    public UploadBuffer CellExtents { get; }

    // the cell of every entry plus one, zero for none. it is written in place, a frame in flight may see one value early, never a torn one.
    public UploadBuffer EntryCells { get; private set; }

    public int Count => _entryCells.Count;
    public bool HasPending => _pending.Count > 0;

    // true when this is another tree, whose entries are other items, so every cell is forgotten.
    public bool SetTree(NamespaceTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (tree == _tree)
            return false;

        _tree = tree;
        _pending.Clear();
        _entryCells.Clear();
        Array.Fill(_cellEntries, Entry.None);
        unsafe
        {
            new Span<byte>((void*)EntryCells.Pointer, (int)EntryCells.Size).Clear();
        }
        return true;
    }

    // the capacity follows the instance buffers, which grow only when the GPU is idle.
    public void EnsureCapacity(uint count)
    {
        if ((ulong)count * sizeof(uint) <= EntryCells.Size)
            return;

        EntryCells.Dispose();
        EntryCells = new UploadBuffer(_device, Math.Max(_minimumCapacity, count) * (ulong)sizeof(uint));
        foreach (var (entry, cell) in _entryCells)
        {
            WriteEntryCell(entry, cell + 1);
        }
    }

    // true when the entry has a picture. a picture once loaded stays on its block however small it gets on screen,
    // touching only tells which cells were wanted lately, the one seen longest ago is the one given away.
    public bool Touch(int entry)
    {
        if (!_entryCells.TryGetValue(entry, out var cell))
            return false;

        _cellSeen[cell] = _frame;
        return true;
    }

    public void Add(Thumbnail thumbnail)
    {
        ArgumentNullException.ThrowIfNull(thumbnail);
        if (thumbnail.Levels.Count == ThumbnailLoader.LevelCount && !_entryCells.ContainsKey(thumbnail.Entry))
        {
            _pending.Enqueue(thumbnail);
        }
    }

    // how many pictures went up this frame.
    public int Record(CommandList list, uint frameSlot)
    {
        ArgumentNullException.ThrowIfNull(list);
        var upload = _uploads[frameSlot];
        var uploaded = 0;
        while (uploaded < _maximumUploadsPerFrame && _pending.TryPeek(out var thumbnail))
        {
            var cell = FindCell();
            if (cell < 0)
                break;

            _pending.Dequeue();
            var previous = _cellEntries[cell];
            if (previous != Entry.None)
            {
                _entryCells.Remove(previous);
                WriteEntryCell(previous, 0);
            }

            list.Transition(Texture, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST);
            CopyCell(list, upload, (ulong)uploaded * _bytesPerThumbnail, thumbnail, cell);
            CellExtents.Write(new Vector2(thumbnail.Width / (float)ThumbnailLoader.CellSize, thumbnail.Height / (float)ThumbnailLoader.CellSize), (ulong)(cell * Unsafe.SizeOf<Vector2>()));
            _cellEntries[cell] = thumbnail.Entry;
            _cellSeen[cell] = _frame;
            _entryCells[thumbnail.Entry] = cell;
            WriteEntryCell(thumbnail.Entry, cell + 1);
            uploaded++;
        }
        list.Transition(Texture, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);

        _frame++;
        return uploaded;
    }

    // a free cell, or else the one seen longest ago, but never one wanted in this very frame.
    private int FindCell()
    {
        var oldest = -1;
        for (var cell = 0; cell < CellCount; cell++)
        {
            if (_cellEntries[cell] == Entry.None)
                return cell;

            if (_cellSeen[cell] < _frame && (oldest < 0 || _cellSeen[cell] < _cellSeen[oldest]))
            {
                oldest = cell;
            }
        }
        return oldest;
    }

    private unsafe void CopyCell(CommandList list, UploadBuffer upload, ulong offset, Thumbnail thumbnail, int cell)
    {
        var slice = (uint)(cell / CellsPerSlice);
        var index = cell % CellsPerSlice;
        for (var level = 0; level < ThumbnailLoader.LevelCount; level++)
        {
            // a row of the upload can be wider than a row of the picture, its pitch is aligned.
            var footprint = _levelFootprints[level];
            footprint.Offset += offset;
            var side = footprint.Footprint.Width;
            var rowBytes = (int)(side * _bytesPerPixel);
            var pixels = thumbnail.Levels[level];
            for (var y = 0; y < side; y++)
            {
                upload.Write<byte>(pixels.AsSpan(y * rowBytes, rowBytes), footprint.Offset + (ulong)y * footprint.Footprint.RowPitch);
            }

            var source = new D3D12_TEXTURE_COPY_LOCATION { pResource = upload.NativePointer, Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT };
            source.Anonymous.PlacedFootprint = footprint;
            var destination = new D3D12_TEXTURE_COPY_LOCATION { pResource = Texture.NativePointer, Type = D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX };
            destination.Anonymous.SubresourceIndex = (uint)level + slice * ThumbnailLoader.LevelCount;
            list.NativeObject.CopyTextureRegion(destination, (uint)(index % CellsPerRow) * side, (uint)(index / CellsPerRow) * side, 0, source, 0);
        }
    }

    private void WriteEntryCell(int entry, int value)
    {
        var offset = (ulong)entry * sizeof(uint);
        if (entry >= 0 && offset + sizeof(uint) <= EntryCells.Size)
        {
            EntryCells.Write((uint)value, offset);
        }
    }

    public void Dispose()
    {
        foreach (var upload in _uploads)
        {
            upload.Dispose();
        }
        CellExtents.Dispose();
        EntryCells.Dispose();
        Texture.Dispose();
    }
}
