namespace Treemapolis.Chrome;

// the brushes and text formats the chrome draws with, built again only when the theme, the material or the monitor scale changes.
public sealed class ChromeResources : IDisposable
{
    private const string _fontFamily = "Segoe UI";
    private const float _captionFontSize = 12.5f;
    private const float _glyphFontSize = 13;
    private const float _statusFontSize = 14;
    private const float _panelFontSize = 12;
    private const uint _islandText = 0xFFF2F5F8;
    private const uint _islandDetail = 0xFFA9B6C4;
    private const string _widthSample = @"C:\Windows\System32\drivers\etc 0123456789";

    private readonly List<IComObject<IDWriteInlineObject>> _trimmingSigns = [];

    public ChromeResources(IComObject<ID2D1DeviceContext> context, IComObject<IDWriteFactory> factory, Glyphs glyphs, Palette palette, bool onMaterial, float scale)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(glyphs);
        ArgumentNullException.ThrowIfNull(palette);
        Factory = factory;
        Glyphs = glyphs;
        Palette = palette;
        OnMaterial = onMaterial;
        Scale = scale;

        CaptionFormat = CreateFormat(factory, _fontFamily, _captionFontSize * scale, DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING);
        CaptionCenterFormat = CreateFormat(factory, _fontFamily, _captionFontSize * scale, DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER);
        CaptionRightFormat = CreateFormat(factory, _fontFamily, _captionFontSize * scale, DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_TRAILING);
        GlyphFormat = CreateFormat(factory, glyphs.Family, _glyphFontSize * scale, DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER);
        StatusFormat = factory.CreateTextFormat(_fontFamily, MathF.Round(_statusFontSize * scale), weight: DWRITE_FONT_WEIGHT.DWRITE_FONT_WEIGHT_SEMI_BOLD);
        PanelFormat = factory.CreateTextFormat(_fontFamily, MathF.Round(_panelFontSize * scale));

        using var sample = factory.CreateTextLayout(CaptionFormat, _widthSample);
        sample.Object.GetMetrics(out var metrics).ThrowOnError();
        CaptionCharacterWidth = MathF.Max(1, metrics.width / _widthSample.Length);

        CaptionBackgroundBrush = context.CreateSolidColorBrush(onMaterial ? palette.CaptionOnMaterial : palette.CaptionBackground);
        TextBrush = context.CreateSolidColorBrush(palette.Text);
        DimTextBrush = context.CreateSolidColorBrush(palette.DimText);
        DisabledTextBrush = context.CreateSolidColorBrush(palette.DisabledText);
        HoverBrush = context.CreateSolidColorBrush(palette.Hover);
        LineBrush = context.CreateSolidColorBrush(palette.Line);
        SelectionBrush = context.CreateSolidColorBrush(palette.Selection);
        PanelBackgroundBrush = context.CreateSolidColorBrush(palette.PanelBackground);
        PanelTextBrush = context.CreateSolidColorBrush(palette.PanelText);
        MenuBackgroundBrush = context.CreateSolidColorBrush(palette.MenuBackground);
        MenuShadowBrush = context.CreateSolidColorBrush(palette.MenuShadow);
        GoodBrush = context.CreateSolidColorBrush(palette.Good);
        BadBrush = context.CreateSolidColorBrush(palette.Bad);
        SwatchBrush = context.CreateSolidColorBrush(palette.Text);
        TrackBrush = context.CreateSolidColorBrush(palette.Line);

        // the island tiles are dark in both themes, their writing is light in both.
        IslandTextBrush = context.CreateSolidColorBrush(new D3DCOLORVALUE(_islandText));
        IslandDetailBrush = context.CreateSolidColorBrush(new D3DCOLORVALUE(_islandDetail));
        ThumbBrush = context.CreateSolidColorBrush(palette.Accent);
    }

    public IComObject<IDWriteFactory> Factory { get; }
    public Glyphs Glyphs { get; }
    public Palette Palette { get; }
    public bool OnMaterial { get; }
    public float Scale { get; }
    public float CaptionCharacterWidth { get; }

    // how long the last chrome frame took, which is what every hover fade advances by.
    public float ElapsedSeconds { get; set; }

    // set by anything still animating, the window draws another chrome frame while it is set.
    public bool Animating { get; set; }

    public IComObject<IDWriteTextFormat> CaptionFormat { get; }
    public IComObject<IDWriteTextFormat> CaptionCenterFormat { get; }
    public IComObject<IDWriteTextFormat> CaptionRightFormat { get; }
    public IComObject<IDWriteTextFormat> GlyphFormat { get; }
    public IComObject<IDWriteTextFormat> StatusFormat { get; }
    public IComObject<IDWriteTextFormat> PanelFormat { get; }

    public IComObject<ID2D1Brush> CaptionBackgroundBrush { get; }
    public IComObject<ID2D1Brush> TextBrush { get; }
    public IComObject<ID2D1Brush> DimTextBrush { get; }
    public IComObject<ID2D1Brush> DisabledTextBrush { get; }
    public IComObject<ID2D1Brush> HoverBrush { get; }
    public IComObject<ID2D1Brush> LineBrush { get; }
    public IComObject<ID2D1Brush> SelectionBrush { get; }
    public IComObject<ID2D1Brush> PanelBackgroundBrush { get; }
    public IComObject<ID2D1Brush> PanelTextBrush { get; }
    public IComObject<ID2D1Brush> MenuBackgroundBrush { get; }
    public IComObject<ID2D1Brush> MenuShadowBrush { get; }
    public IComObject<ID2D1Brush> GoodBrush { get; }
    public IComObject<ID2D1Brush> BadBrush { get; }

    // recolored for every swatch it fills, a legend is a dozen of them.
    public IComObject<ID2D1Brush> SwatchBrush { get; }
    public IComObject<ID2D1Brush> TrackBrush { get; }
    public IComObject<ID2D1Brush> IslandTextBrush { get; }
    public IComObject<ID2D1Brush> IslandDetailBrush { get; }
    public IComObject<ID2D1Brush> ThumbBrush { get; }

    public void FillHover(IComObject<ID2D1DeviceContext> context, in D2D_RECT_F rect, float opacity, float radius = 0) => FillFaded(context, HoverBrush, rect, opacity, radius);

    public static void FillFaded(IComObject<ID2D1DeviceContext> context, IComObject<ID2D1Brush> brush, in D2D_RECT_F rect, float opacity, float radius = 0)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(brush);
        if (opacity <= 0)
            return;

        brush.Object.SetOpacity(opacity);
        if (radius > 0)
        {
            context.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = rect, radiusX = radius, radiusY = radius }, brush.Object);
        }
        else
        {
            context.Object.FillRectangle(rect, brush.Object);
        }
        brush.Object.SetOpacity(1);
    }

    public static unsafe void DrawText(IComObject<ID2D1DeviceContext> context, ReadOnlySpan<char> text, IComObject<IDWriteTextFormat> format, in D2D_RECT_F rect, IComObject<ID2D1Brush> brush)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(brush);
        if (text.Length == 0)
            return;

        fixed (char* pointer = text)
        {
            context.Object.DrawText(new PWSTR { Value = (nint)pointer }, (uint)text.Length, format.Object, rect, brush.Object, D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_CLIP, DWRITE_MEASURING_MODE.DWRITE_MEASURING_MODE_NATURAL);
        }
    }

    // text too long for its room ends with an ellipsis rather than a glyph cut in half.
    private IComObject<IDWriteTextFormat> CreateFormat(IComObject<IDWriteFactory> factory, string family, float size, DWRITE_TEXT_ALIGNMENT alignment)
    {
        var format = factory.CreateTextFormat(family, MathF.Round(size));
        format.Object.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_CENTER).ThrowOnError();
        format.Object.SetWordWrapping(DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP).ThrowOnError();
        format.Object.SetTextAlignment(alignment).ThrowOnError();
        var sign = factory.CreateEllipsisTrimmingSign(format);
        _trimmingSigns.Add(sign);
        format.Object.SetTrimming(new DWRITE_TRIMMING { granularity = DWRITE_TRIMMING_GRANULARITY.DWRITE_TRIMMING_GRANULARITY_CHARACTER }, sign.Object).ThrowOnError();
        return format;
    }

    public void Dispose()
    {
        CaptionFormat.Dispose();
        CaptionCenterFormat.Dispose();
        CaptionRightFormat.Dispose();
        GlyphFormat.Dispose();
        StatusFormat.Dispose();
        PanelFormat.Dispose();
        CaptionBackgroundBrush.Dispose();
        TextBrush.Dispose();
        DimTextBrush.Dispose();
        DisabledTextBrush.Dispose();
        HoverBrush.Dispose();
        LineBrush.Dispose();
        SelectionBrush.Dispose();
        PanelBackgroundBrush.Dispose();
        PanelTextBrush.Dispose();
        MenuBackgroundBrush.Dispose();
        MenuShadowBrush.Dispose();
        GoodBrush.Dispose();
        BadBrush.Dispose();
        SwatchBrush.Dispose();
        TrackBrush.Dispose();
        IslandTextBrush.Dispose();
        IslandDetailBrush.Dispose();
        ThumbBrush.Dispose();
        foreach (var sign in _trimmingSigns)
        {
            sign.Dispose();
        }
    }
}
