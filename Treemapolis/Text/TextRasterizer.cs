namespace Treemapolis.Text;

// DirectWrite lays the text out and Direct2D draws it white on a transparent WIC bitmap, the coverage becomes an alpha channel.
// it is software rendering on purpose, a few hundred short strings a second cost nothing and need no device of their own.
public sealed class TextRasterizer : IDisposable
{
    private const string _fontFamily = "Segoe UI";
    private const float _padding = 2;
    private const float _containerFontSize = 30;
    private const float _fileFontSize = 22;

    private readonly IComObject<IDWriteFactory> _dwrite;
    private readonly WicBitmapSource _bitmap;
    private readonly IComObject<ID2D1RenderTarget> _target;
    private readonly IComObject<ID2D1SolidColorBrush> _brush;
    private readonly Dictionary<TextStyle, IComObject<IDWriteTextFormat>> _formats = [];

    public TextRasterizer(uint maxWidth, uint maxHeight)
    {
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
        _dwrite = DWriteFunctions.DWriteCreateFactory();
        _bitmap = new WicBitmapSource(maxWidth, maxHeight, WicPixelFormat.GUID_WICPixelFormat32bppPBGRA);
        _target = _bitmap.CreateRenderTarget();
        _target.Object.SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE.D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
        _brush = _target.CreateSolidColorBrush(new D3DCOLORVALUE(1, 1, 1, 1));
    }

    public uint MaxWidth { get; }
    public uint MaxHeight { get; }

    // copies the coverage row by row into destination, which is width bytes a row, and returns false when the text does not fit.
    public unsafe bool Rasterize(string text, TextStyle style, byte[] destination, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(destination);
        width = 0;
        height = 0;
        if (text.Length == 0)
            return false;

        using var layout = _dwrite.CreateTextLayout(GetFormat(style), text, 0, MaxWidth - 2 * _padding, MaxHeight - 2 * _padding);
        var metrics = layout.GetMetrics();
        width = (int)MathF.Ceiling(metrics.widthIncludingTrailingWhitespace + 2 * _padding);
        height = (int)MathF.Ceiling(metrics.height + 2 * _padding);
        if (width > MaxWidth || height > MaxHeight || width * height > destination.Length)
            return false;

        var target = _target.Object;
        target.BeginDraw();
        target.Clear(new D3DCOLORVALUE(0, 0, 0, 0));
        target.DrawTextLayout(new D2D_POINT_2F(_padding, _padding), layout.Object, _brush.Object, D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE);
        target.EndDraw(0, 0).ThrowOnError();

        var copyWidth = width;
        var copyHeight = height;
        _bitmap.WithLock(WICBitmapLockFlags.WICBitmapLockRead, bitmapLock =>
        {
            var pixels = new ReadOnlySpan<byte>((void*)bitmapLock.DataPointer, (int)bitmapLock.DataSize);
            for (var y = 0; y < copyHeight; y++)
            {
                var row = pixels.Slice(y * (int)bitmapLock.Stride, copyWidth * 4);
                for (var x = 0; x < copyWidth; x++)
                {
                    // premultiplied BGRA, the alpha is the fourth byte.
                    destination[y * copyWidth + x] = row[x * 4 + 3];
                }
            }
        }, new WICRect { Width = copyWidth, Height = copyHeight });
        return true;
    }

    private IComObject<IDWriteTextFormat> GetFormat(TextStyle style)
    {
        if (_formats.TryGetValue(style, out var format))
            return format;

        var size = style == TextStyle.Container ? _containerFontSize : _fileFontSize;
        var weight = style == TextStyle.Container ? DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_SEMI_BOLD : DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_NORMAL;
        format = _dwrite.CreateTextFormat(_fontFamily, size, weight: weight);
        format.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP).ThrowOnError();

        _formats[style] = format;
        return format;
    }

    public void Dispose()
    {
        foreach (var format in _formats.Values)
        {
            format.Dispose();
        }

        _brush.Dispose();
        _target.Dispose();
        _bitmap.Dispose();
        _dwrite.Dispose();
    }
}
