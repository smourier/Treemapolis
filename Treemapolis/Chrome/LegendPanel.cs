namespace Treemapolis.Chrome;

// what the colors of the map mean, for the color mode in use.
public sealed class LegendPanel
{
    private const float _padding = 8;
    private const float _radius = 6;
    private const float _rowHeight = 18;
    private const float _swatchWidth = 22;
    private const float _swatchHeight = 11;
    private const float _swatchGap = 8;
    private const float _swatchRadius = 2;
    private const float _minimumTextWidth = 140;
    private const uint _opaque = 0xFF000000;

    private IReadOnlyList<LegendEntry> _entries = [];
    private ColorMode? _colorMode;

    public bool IsVisible { get; set; } = true;
    public IReadOnlyList<LegendEntry> Entries => _entries;

    // true when the color mode changed, which is what asks for a chrome frame.
    public bool SetColorMode(ColorMode colorMode)
    {
        if (_colorMode == colorMode)
            return false;

        _colorMode = colorMode;
        _entries = TreemapLayout.GetLegend(colorMode);
        return true;
    }

    public D2D_SIZE_F Measure(ChromeResources resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var scale = resources.Scale;
        var longest = 0;
        foreach (var entry in _entries)
        {
            longest = Math.Max(longest, entry.Label.Length);
        }

        var textWidth = MathF.Max(_minimumTextWidth * scale, longest * resources.CaptionCharacterWidth);
        var width = (2 * _padding + _swatchWidth + _swatchGap) * scale + textWidth;
        var height = 2 * _padding * scale + (_entries.Count + 1) * _rowHeight * scale;
        return new D2D_SIZE_F(MathF.Ceiling(width), MathF.Ceiling(height));
    }

    public void Render(IComObject<ID2D1DeviceContext> context, ChromeResources resources, D2D_POINT_2F position)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resources);
        if (!IsVisible || _entries.Count == 0)
            return;

        var native = context.Object;
        var scale = resources.Scale;
        var size = Measure(resources);
        var radius = _radius * scale;
        var panel = new D2D_RECT_F { left = position.x, top = position.y, right = position.x + size.width, bottom = position.y + size.height };
        native.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = panel, radiusX = radius, radiusY = radius }, resources.PanelBackgroundBrush.Object);

        var left = position.x + _padding * scale;
        var top = position.y + _padding * scale;
        var rowHeight = _rowHeight * scale;
        var title = _colorMode == ColorMode.Age ? Res.LegendTitleAge : Res.LegendTitleType;
        ChromeResources.DrawText(context, title, resources.CaptionFormat, new D2D_RECT_F { left = left, top = top, right = panel.right, bottom = top + rowHeight }, resources.PanelTextBrush);

        var swatchBrush = (ID2D1SolidColorBrush)resources.SwatchBrush.Object;
        foreach (var entry in _entries)
        {
            top += rowHeight;
            var middle = top + rowHeight / 2;
            var swatch = new D2D_RECT_F { left = left, top = middle - _swatchHeight * scale / 2, right = left + _swatchWidth * scale, bottom = middle + _swatchHeight * scale / 2 };
            var swatchRadius = _swatchRadius * scale;
            if (entry.Color == entry.EndColor)
            {
                swatchBrush.SetColor(ToColor(entry.Color));
                native.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = swatch, radiusX = swatchRadius, radiusY = swatchRadius }, swatchBrush);
            }
            else
            {
                // a range shows its two ends side by side.
                var half = swatch with { right = (swatch.left + swatch.right) / 2 };
                swatchBrush.SetColor(ToColor(entry.Color));
                native.FillRectangle(half, swatchBrush);
                swatchBrush.SetColor(ToColor(entry.EndColor));
                native.FillRectangle(swatch with { left = half.right }, swatchBrush);
            }

            var text = new D2D_RECT_F { left = swatch.right + _swatchGap * scale, top = top, right = panel.right - _padding * scale, bottom = top + rowHeight };
            ChromeResources.DrawText(context, entry.Label, resources.CaptionFormat, text, resources.PanelTextBrush);
        }
    }

    private static D3DCOLORVALUE ToColor(uint rgb) => new(_opaque | rgb);
}
