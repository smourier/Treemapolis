namespace Treemapolis.Text;

// strings packed on shelves of a single coverage texture, keyed by style and text so a hundred folders named bin share one entry.
// when it is full it starts over, the labels in view are simply rasterized again.
public sealed class LabelAtlas(GraphicsDevice device, DescriptorHeaps heaps, uint frameSlots) : IDisposable
{
    private const uint _size = 4096;
    private const uint _maxLabelWidth = 1024;
    private const uint _maxLabelHeight = 64;
    private const int _gutter = 2;

    private readonly TextRasterizer _rasterizer = new(_maxLabelWidth, _maxLabelHeight);
    private readonly byte[] _scratch = new byte[_maxLabelWidth * _maxLabelHeight];
    private readonly Dictionary<LabelKey, AtlasEntry> _entries = [];
    private int _shelfX;
    private int _shelfY;
    private int _shelfHeight;

    public TextTexture Texture { get; } = new TextTexture(device, heaps, _size, _size, frameSlots);
    public int Generation { get; private set; }
    public int Count => _entries.Count;
    public static uint Size => _size;

    public bool TryGet(LabelKey key, out AtlasEntry entry) => _entries.TryGetValue(key, out entry);

    // false means the atlas is full, the caller resets it and tries again on its next pass.
    public bool TryAdd(LabelKey key, string text, out AtlasEntry entry)
    {
        entry = default;
        if (!_rasterizer.Rasterize(text, key.Style, _scratch, out var width, out var height))
        {
            // text that can never fit gets an empty entry, so it is not rasterized again every pass.
            _entries[key] = default;
            return true;
        }

        if (_shelfX + width + _gutter > _size)
        {
            _shelfY += _shelfHeight + _gutter;
            _shelfX = 0;
            _shelfHeight = 0;
        }

        if (_shelfY + height > _size)
            return false;

        var pixels = Texture.Pixels;
        for (var y = 0; y < height; y++)
        {
            _scratch.AsSpan(y * width, width).CopyTo(pixels.AsSpan((_shelfY + y) * (int)_size + _shelfX, width));
        }

        entry = new AtlasEntry(_shelfX, _shelfY, width, height);
        _entries[key] = entry;
        Texture.MarkDirty(_shelfY, _shelfY + height);
        _shelfX += width + _gutter;
        _shelfHeight = Math.Max(_shelfHeight, height);
        return true;
    }

    public void Reset()
    {
        _entries.Clear();
        _shelfX = 0;
        _shelfY = 0;
        _shelfHeight = 0;
        Generation++;
    }

    public void Dispose()
    {
        _rasterizer.Dispose();
        Texture.Dispose();
    }
}
