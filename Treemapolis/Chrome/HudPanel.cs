namespace Treemapolis.Chrome;

// a block of text on a rounded panel. its layout is built once per text and per resources, not on every chrome frame.
public sealed class HudPanel(bool emphasized) : IDisposable
{
    private const float _padding = 8;
    private const float _radius = 6;
    private const float _maximumWidth = 1600;

    private IComObject<IDWriteTextLayout>? _layout;
    private ChromeResources? _layoutResources;
    private string _text = string.Empty;

    public bool IsVisible { get; set; } = true;
    public string Text => _text;

    // true when the text changed, which is what asks for a chrome frame.
    public bool SetText(string? text)
    {
        text ??= string.Empty;
        if (text == _text)
            return false;

        _text = text;
        DisposeLayout();
        return true;
    }

    public D2D_SIZE_F Measure(ChromeResources resources)
    {
        var layout = GetLayout(resources);
        if (layout == null)
            return default;

        var metrics = layout.GetMetrics();
        var padding = _padding * resources.Scale;
        return new D2D_SIZE_F(MathF.Ceiling(metrics.widthIncludingTrailingWhitespace + 2 * padding), MathF.Ceiling(metrics.height + 2 * padding));
    }

    public void Render(IComObject<ID2D1DeviceContext> context, ChromeResources resources, D2D_POINT_2F position)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resources);
        var layout = GetLayout(resources);
        if (!IsVisible || layout == null)
            return;

        var size = Measure(resources);
        var radius = _radius * resources.Scale;
        var rect = new D2D_RECT_F { left = position.x, top = position.y, right = position.x + size.width, bottom = position.y + size.height };
        context.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = rect, radiusX = radius, radiusY = radius }, resources.PanelBackgroundBrush.Object);
        var padding = _padding * resources.Scale;
        context.Object.DrawTextLayout(new D2D_POINT_2F(position.x + padding, position.y + padding), layout.Object, resources.PanelTextBrush.Object, D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE);
    }

    private IComObject<IDWriteTextLayout>? GetLayout(ChromeResources resources)
    {
        if (_text.Length == 0)
            return null;

        if (_layout != null && _layoutResources == resources)
            return _layout;

        DisposeLayout();
        _layout = resources.Factory.CreateTextLayout(emphasized ? resources.StatusFormat : resources.PanelFormat, _text, 0, _maximumWidth * resources.Scale, _maximumWidth * resources.Scale);
        _layoutResources = resources;
        return _layout;
    }

    private void DisposeLayout()
    {
        _layout?.Dispose();
        _layout = null;
        _layoutResources = null;
    }

    public void Dispose() => DisposeLayout();
}
