namespace Treemapolis.Chrome;

// shown over the map while a scan has nothing to show yet: a spinner, what is being read, and how far through it when that is known.
public sealed class LoadingPanel : IDisposable
{
    private const float _padding = 16;
    private const float _radius = 8;
    private const float _spinnerOuter = 13;
    private const float _spinnerInner = 7;
    private const float _spokeWidth = 2.5f;
    private const int _spokeCount = 12;
    private const float _turnsPerSecond = 1;
    private const float _minimumSpokeOpacity = 0.15f;
    private const float _gap = 12;
    private const float _lineGap = 4;
    private const float _barHeight = 4;
    private const float _minimumTextWidth = 240;

    private IComObject<IDWriteTextLayout>? _titleLayout;
    private IComObject<IDWriteTextLayout>? _detailLayout;
    private ChromeResources? _layoutResources;
    private string _title = string.Empty;
    private string? _detail;

    public bool IsVisible { get; private set; }

    // progress from zero to one, or below zero when how much is left is not known.
    public float Progress { get; private set; } = -1;

    // true when what the panel shows changed, which is what asks for a chrome frame.
    public bool Set(bool visible, string title, string? detail, float progress)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (visible == IsVisible && title == _title && detail == _detail && progress == Progress)
            return false;

        if (title != _title || detail != _detail)
        {
            DisposeLayouts();
        }

        IsVisible = visible;
        _title = title;
        _detail = detail;
        Progress = progress;
        return true;
    }

    public void Render(IComObject<ID2D1DeviceContext> context, ChromeResources resources, D2D_POINT_2F center, double seconds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resources);
        if (!IsVisible)
            return;

        EnsureLayouts(resources);
        var scale = resources.Scale;
        var title = Measure(_titleLayout!);
        var detail = _detailLayout != null ? Measure(_detailLayout) : default;
        var spinner = _spinnerOuter * 2 * scale;
        var textWidth = MathF.Max(_minimumTextWidth * scale, MathF.Max(title.width, detail.width));
        var textHeight = title.height + (_detailLayout != null ? _lineGap * scale + detail.height : 0);
        var contentHeight = MathF.Max(spinner, textHeight);
        var barSpace = Progress >= 0 ? (_gap + _barHeight) * scale : 0;
        var padding = _padding * scale;
        var width = padding * 2 + spinner + _gap * scale + textWidth;
        var height = padding * 2 + contentHeight + barSpace;
        var rect = new D2D_RECT_F { left = MathF.Round(center.x - width / 2), top = MathF.Round(center.y - height / 2) };
        rect.right = rect.left + width;
        rect.bottom = rect.top + height;
        var radius = _radius * scale;
        context.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = rect, radiusX = radius, radiusY = radius }, resources.PanelBackgroundBrush.Object);

        // the brightest spoke goes round once a second, the ones behind it fade out.
        var spinnerCenter = new D2D_POINT_2F(rect.left + padding + spinner / 2, rect.top + padding + contentHeight / 2);
        var head = (float)(seconds * _turnsPerSecond % 1) * _spokeCount;
        var brush = resources.PanelTextBrush;
        for (var i = 0; i < _spokeCount; i++)
        {
            var angle = i * MathF.Tau / _spokeCount;
            var behind = (head - i + _spokeCount) % _spokeCount;
            brush.Object.SetOpacity(MathF.Max(_minimumSpokeOpacity, 1 - behind / _spokeCount));
            var direction = new Vector2(MathF.Sin(angle), -MathF.Cos(angle));
            var inner = direction * _spinnerInner * scale;
            var outer = direction * _spinnerOuter * scale;
            context.Object.DrawLine(new D2D_POINT_2F(spinnerCenter.x + inner.X, spinnerCenter.y + inner.Y), new D2D_POINT_2F(spinnerCenter.x + outer.X, spinnerCenter.y + outer.Y), brush.Object, _spokeWidth * scale, null);
        }
        brush.Object.SetOpacity(1);

        var textLeft = rect.left + padding + spinner + _gap * scale;
        var textTop = rect.top + padding + (contentHeight - textHeight) / 2;
        context.Object.DrawTextLayout(new D2D_POINT_2F(textLeft, textTop), _titleLayout!.Object, resources.PanelTextBrush.Object, D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE);
        if (_detailLayout != null)
        {
            context.Object.DrawTextLayout(new D2D_POINT_2F(textLeft, textTop + title.height + _lineGap * scale), _detailLayout.Object, resources.PanelTextBrush.Object, D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE);
        }

        if (Progress >= 0)
        {
            var bar = new D2D_RECT_F { left = rect.left + padding, right = rect.right - padding, bottom = rect.bottom - padding };
            bar.top = bar.bottom - _barHeight * scale;
            var barRadius = _barHeight * scale / 2;
            context.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = bar, radiusX = barRadius, radiusY = barRadius }, resources.TrackBrush.Object);
            bar.right = bar.left + (bar.right - bar.left) * Math.Clamp(Progress, 0, 1);
            context.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = bar, radiusX = barRadius, radiusY = barRadius }, resources.ThumbBrush.Object);
        }
        resources.Animating = true;
    }

    private void EnsureLayouts(ChromeResources resources)
    {
        if (_titleLayout != null && _layoutResources == resources)
            return;

        DisposeLayouts();
        _titleLayout = resources.Factory.CreateTextLayout(resources.StatusFormat, _title);
        if (_detail != null)
        {
            _detailLayout = resources.Factory.CreateTextLayout(resources.PanelFormat, _detail);
        }
        _layoutResources = resources;
    }

    private static D2D_SIZE_F Measure(IComObject<IDWriteTextLayout> layout)
    {
        layout.Object.GetMetrics(out var metrics).ThrowOnError();
        return new D2D_SIZE_F(MathF.Ceiling(metrics.widthIncludingTrailingWhitespace), MathF.Ceiling(metrics.height));
    }

    private void DisposeLayouts()
    {
        _titleLayout?.Dispose();
        _titleLayout = null;
        _detailLayout?.Dispose();
        _detailLayout = null;
        _layoutResources = null;
    }

    public void Dispose() => DisposeLayouts();
}
