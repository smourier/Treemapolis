namespace Treemapolis.Chrome;

// the window draws its own caption. the standard one is removed by taking the whole window as client area in WM_NCCALCSIZE,
// and what Windows still needs to know, the drag area and the three window buttons, is handed back through WM_NCHITTEST.
// real hit test codes are what keep double click to maximize and the snap layouts flyout working.
// the navigation buttons and the gear report themselves as client area, so they get ordinary mouse messages instead of dragging the window.
public sealed class TitleBar : Control
{
    public const int HitClient = 1;
    public const int HitCaption = 2;
    public const int HitMinimize = 8;
    public const int HitMaximize = 9;
    public const int HitTop = 12;
    public const int HitClose = 20;

    private const float _height = 32;
    private const float _buttonWidth = 46;
    private const float _navigationWidth = 34;
    private const float _gearWidth = 34;
    private const float _windowButtonCount = 3;
    private const float _titleGap = 8;
    private const float _statusGap = 16;
    private const float _statusShare = 0.5f;
    private const float _glyphSize = 10;
    private const float _inset = 3;
    private const float _radius = 4;
    private const float _brandPadding = 10;
    private const float _brandGap = 8;
    private const float _crumbPadding = 6;
    private const float _chevronWidth = 18;
    private const string _overflowText = "…";

    private static readonly CaptionButton[] _navigation = [CaptionButton.Back, CaptionButton.Forward, CaptionButton.Up, CaptionButton.Reveal, CaptionButton.Hidden, CaptionButton.FrameAll, CaptionButton.ColorMode, CaptionButton.Search];

    // one per button in drawing order, so each fades on its own rather than the row flashing together.
    private readonly HoverAnimation[] _navigationHovers = new HoverAnimation[_navigation.Length];
    private HoverAnimation _gearHover;
    private HoverAnimation _elevateHover;
    private HoverAnimation _minimizeHover;
    private HoverAnimation _maximizeHover;
    private HoverAnimation _closeHover;
    private float _scale = 1;
    private IReadOnlyList<Crumb> _crumbs = [];
    private float[] _crumbWidths = [];
    private float _overflowWidth;
    private ChromeResources? _crumbResources;
    private readonly List<CrumbPart> _crumbParts = [];
    private int _hotCrumbPart = -1;
    private HoverAnimation _crumbHover;
    private string? _measuredStatus;
    private ChromeResources? _measuredResources;
    private float _statusWidth;

    // the icon and the name of the application ahead of the navigation buttons, measured when the caption is drawn.
    private float _brandWidth;

    public int HotWindowButton { get; set; }
    public CaptionButton HotButton { get; private set; }
    public bool IsMaximized { get; set; }
    public bool IsFullScreen { get; set; }
    public bool BackEnabled { get; set; }
    public bool ForwardEnabled { get; set; }
    public bool UpEnabled { get; set; }
    public bool RevealEnabled { get; set; }
    public bool ShowHidden { get; set; }
    public bool IsElevated { get; set; }
    public IReadOnlyList<Crumb> Crumbs => _crumbs;
    public int HiddenCrumbCount { get; private set; }
    public Action<int, bool, D2D_RECT_F>? CrumbPressed { get; set; }
    public string Status { get; set; } = string.Empty;
    public Action<CaptionButton>? Pressed { get; set; }
    public float Height => MathF.Round(_height * _scale);
    public string? TooltipText => HotWindowButton switch
    {
        HitMinimize => Res.CaptionMinimize,
        HitMaximize => IsFullScreen ? Res.CaptionExitFullScreen : IsMaximized ? Res.CaptionRestore : Res.CaptionMaximize,
        HitClose => Res.CaptionClose,
        _ => HotButton switch
        {
            CaptionButton.Back => Res.CaptionBack,
            CaptionButton.Forward => Res.CaptionForward,
            CaptionButton.Up => Res.CaptionUp,
            CaptionButton.Reveal => Res.CaptionReveal,
            CaptionButton.Hidden => ShowHidden ? Res.CaptionHideHidden : Res.CaptionShowHidden,
            CaptionButton.FrameAll => Res.CaptionFrameAll,
            CaptionButton.ColorMode => Res.CaptionColorMode,
            CaptionButton.Search => Res.CaptionSearch,
            CaptionButton.Elevate => IsElevated ? Res.CaptionElevated : Res.CaptionElevate,
            CaptionButton.Settings => Res.CaptionSettings,
            _ => null,
        },
    };

    public D2D_RECT_F TooltipAnchor
    {
        get
        {
            var width = _buttonWidth * _scale;
            switch (HotWindowButton)
            {
                case HitMinimize:
                    return new D2D_RECT_F { left = Bounds.right - width * 3, top = Bounds.top, right = Bounds.right - width * 2, bottom = Bounds.bottom };

                case HitMaximize:
                    return new D2D_RECT_F { left = Bounds.right - width * 2, top = Bounds.top, right = Bounds.right - width, bottom = Bounds.bottom };

                case HitClose:
                    return new D2D_RECT_F { left = Bounds.right - width, top = Bounds.top, right = Bounds.right, bottom = Bounds.bottom };
            }

            if (HotButton == CaptionButton.Settings)
                return GearBounds;

            if (HotButton == CaptionButton.Elevate)
                return new D2D_RECT_F { left = ElevateLeft, top = Bounds.top, right = GearLeft, bottom = Bounds.bottom };

            var index = Array.IndexOf(_navigation, HotButton);
            var left = NavigationLeft + _navigationWidth * _scale * Math.Max(0, index);
            return new D2D_RECT_F { left = left, top = Bounds.top, right = left + _navigationWidth * _scale, bottom = Bounds.bottom };
        }
    }

    private float NavigationLeft => Bounds.left + _brandWidth;
    private float NavigationRight => NavigationLeft + _navigationWidth * _navigation.Length * _scale;
    private float GearLeft => Bounds.right - (_buttonWidth * _windowButtonCount + _gearWidth) * _scale;
    private float ElevateLeft => GearLeft - _gearWidth * _scale;
    public D2D_RECT_F GearBounds => new() { left = GearLeft, top = Bounds.top, right = GearLeft + _gearWidth * _scale, bottom = Bounds.bottom };

    public void Update(in D2D_RECT_F client, float scale)
    {
        _scale = scale;
        Bounds = new D2D_RECT_F { left = client.left, top = client.top, right = client.right, bottom = client.top + Height };
    }

    public int HitTest(float x, float y)
    {
        if (!Contains(x, y))
            return HitClient;

        // the icon and the name drag the window like the rest of the caption.
        if (x >= NavigationLeft && x < NavigationRight || CrumbPartAt(x, y) >= 0 || ButtonAt(x, y) is CaptionButton.Settings or CaptionButton.Elevate)
            return HitClient;

        var width = _buttonWidth * _scale;
        if (x >= Bounds.right - width)
            return HitClose;

        if (x >= Bounds.right - width * 2)
            return HitMaximize;

        if (x >= Bounds.right - width * 3)
            return HitMinimize;

        return HitCaption;
    }

    public CaptionButton ButtonAt(float x, float y)
    {
        if (!Contains(x, y))
            return CaptionButton.None;

        if (x >= GearLeft && x < GearLeft + _gearWidth * _scale)
            return CaptionButton.Settings;

        if (x >= ElevateLeft && x < GearLeft)
            return CaptionButton.Elevate;

        if (x < NavigationLeft || x >= NavigationRight)
            return CaptionButton.None;

        return _navigation[Math.Clamp((int)((x - NavigationLeft) / (_navigationWidth * _scale)), 0, _navigation.Length - 1)];
    }

    public bool IsEnabled(CaptionButton button) => button switch
    {
        CaptionButton.Back => BackEnabled,
        CaptionButton.Forward => ForwardEnabled,
        CaptionButton.Up => UpEnabled,
        CaptionButton.Reveal => RevealEnabled,
        CaptionButton.Elevate => !IsElevated,
        CaptionButton.None => false,
        _ => true,
    };

    public override bool OnMouseMove(float x, float y)
    {
        var changed = SetHotCrumbPart(CrumbPartAt(x, y));
        var button = ButtonAt(x, y);
        if (button == HotButton)
            return changed;

        HotButton = button;
        return true;
    }

    public override void OnMouseLeave()
    {
        HotButton = CaptionButton.None;
        SetHotCrumbPart(-1);
    }

    public override bool OnMouseDown(float x, float y)
    {
        var part = CrumbPartAt(x, y);
        if (part >= 0)
        {
            var pressed = _crumbParts[part];
            CrumbPressed?.Invoke(pressed.Index, pressed.IsChevron, pressed.Rect);
            return true;
        }

        var button = ButtonAt(x, y);
        if (!IsEnabled(button))
            return Contains(x, y);

        Pressed?.Invoke(button);
        return true;
    }

    public unsafe void Render(IComObject<ID2D1DeviceContext> context, ChromeResources resources)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resources);
        context.Object.FillRectangle(Bounds, resources.CaptionBackgroundBrush.Object);

        var iconSize = MathF.Round(ChromeResources.AppIconSize * _scale);
        var iconLeft = MathF.Round(Bounds.left + _brandPadding * _scale);
        var iconTop = MathF.Round((Bounds.top + Bounds.bottom - iconSize) / 2);
        if (resources.AppIcon != null)
        {
            var destination = new D2D_RECT_F { left = iconLeft, top = iconTop, right = iconLeft + iconSize, bottom = iconTop + iconSize };
            context.Object.DrawBitmap(resources.AppIcon.Object, (nint)(&destination), 1, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC, 0, 0);
        }

        var nameLeft = iconLeft + iconSize + _brandGap * _scale;
        ChromeResources.DrawText(context, Res.WindowTitle, resources.AppNameFormat, new D2D_RECT_F { left = nameLeft, top = Bounds.top, right = nameLeft + resources.AppNameWidth, bottom = Bounds.bottom }, resources.TextBrush);
        _brandWidth = nameLeft + resources.AppNameWidth + _brandPadding * _scale - Bounds.left;

        var glyphs = resources.Glyphs;
        for (var i = 0; i < _navigation.Length; i++)
        {
            var button = _navigation[i];
            var glyph = button switch
            {
                CaptionButton.Back => glyphs.Back,
                CaptionButton.Forward => glyphs.Forward,
                CaptionButton.Up => glyphs.Up,
                CaptionButton.Reveal => glyphs.Reveal,
                CaptionButton.FrameAll => glyphs.FrameAll,
                CaptionButton.Hidden => glyphs.Hidden,
                CaptionButton.Search => glyphs.Search,
                _ => glyphs.ColorMode,
            };
            var left = NavigationLeft + _navigationWidth * _scale * i;
            DrawGlyphButton(context, resources, ref _navigationHovers[i], button, glyph, left, _navigationWidth);
        }

        // the status keeps up to half of the room between the buttons, trimmed past that, and the path fits into the rest.
        var titleLeft = NavigationRight + _titleGap * _scale;
        var statusRight = ElevateLeft - _titleGap * _scale;
        var statusWidth = MathF.Min(MeasureStatus(resources), MathF.Max(0, statusRight - titleLeft) * _statusShare);
        var statusRect = new D2D_RECT_F { left = statusRight - statusWidth, top = Bounds.top, right = statusRight, bottom = Bounds.bottom };
        ChromeResources.DrawText(context, Status, resources.CaptionRightFormat, statusRect, resources.DimTextBrush);

        var titleRect = new D2D_RECT_F { left = titleLeft, top = Bounds.top, right = statusRect.left - (statusWidth > 0 ? _statusGap : _titleGap) * _scale, bottom = Bounds.bottom };
        RenderCrumbs(context, resources, titleRect);

        DrawGlyphButton(context, resources, ref _elevateHover, CaptionButton.Elevate, glyphs.Shield, ElevateLeft, _gearWidth);
        DrawGlyphButton(context, resources, ref _gearHover, CaptionButton.Settings, glyphs.Settings, GearLeft, _gearWidth);
        var width = _buttonWidth * _scale;
        DrawWindowButton(context, resources, HitMinimize, Bounds.right - width * 3, width);
        DrawWindowButton(context, resources, HitMaximize, Bounds.right - width * 2, width);
        DrawWindowButton(context, resources, HitClose, Bounds.right - width, width);
        context.Object.DrawLine(new D2D_POINT_2F(Bounds.left, Bounds.bottom - 0.5f), new D2D_POINT_2F(Bounds.right, Bounds.bottom - 0.5f), resources.LineBrush.Object, 1, null);
    }

    public bool SetCrumbs(IReadOnlyList<Crumb> crumbs)
    {
        ArgumentNullException.ThrowIfNull(crumbs);
        if (crumbs.SequenceEqual(_crumbs))
            return false;

        _crumbs = crumbs;
        _crumbResources = null;
        _crumbParts.Clear();
        _hotCrumbPart = -1;
        return true;
    }

    private void RenderCrumbs(IComObject<ID2D1DeviceContext> context, ChromeResources resources, in D2D_RECT_F area)
    {
        _crumbParts.Clear();
        HiddenCrumbCount = 0;
        if (_crumbs.Count == 0 || area.right <= area.left)
            return;

        MeasureCrumbs(resources);
        var padding = _crumbPadding * _scale;
        var chevron = _chevronWidth * _scale;
        var total = 0f;
        for (var i = 0; i < _crumbs.Count; i++)
        {
            total += _crumbWidths[i] + 2 * padding + chevron;
        }

        var first = 0;
        var available = area.right - area.left;
        while (first < _crumbs.Count - 1 && total + (first > 0 ? _overflowWidth + 2 * padding : 0) > available)
        {
            total -= _crumbWidths[first] + 2 * padding + chevron;
            first++;
        }
        HiddenCrumbCount = first;

        var x = area.left;
        if (first > 0)
        {
            var width = _overflowWidth + 2 * padding;
            _crumbParts.Add(new CrumbPart(-1, false, new D2D_RECT_F { left = x, top = Bounds.top, right = x + width, bottom = Bounds.bottom }));
            x += width;
        }

        for (var i = first; i < _crumbs.Count; i++)
        {
            var width = MathF.Min(_crumbWidths[i] + 2 * padding, MathF.Max(0, area.right - chevron - x));
            _crumbParts.Add(new CrumbPart(i, false, new D2D_RECT_F { left = x, top = Bounds.top, right = x + width, bottom = Bounds.bottom }));
            x += width;
            _crumbParts.Add(new CrumbPart(i, true, new D2D_RECT_F { left = x, top = Bounds.top, right = x + chevron, bottom = Bounds.bottom }));
            x += chevron;
        }

        if (_crumbHover.Advance(_hotCrumbPart >= 0, resources.ElapsedSeconds))
        {
            resources.Animating = true;
        }

        var inset = _inset * _scale;
        var glyphs = resources.Glyphs;
        for (var i = 0; i < _crumbParts.Count; i++)
        {
            var part = _crumbParts[i];
            var rect = new D2D_RECT_F { left = part.Rect.left, top = part.Rect.top + inset, right = part.Rect.right, bottom = part.Rect.bottom - inset };
            if (i == _hotCrumbPart)
            {
                resources.FillHover(context, rect, _crumbHover.Opacity, _radius * _scale);
            }

            if (part.IsChevron)
            {
                ReadOnlySpan<char> text = [glyphs.Submenu];
                ChromeResources.DrawText(context, text, resources.GlyphFormat, rect, resources.DimTextBrush);
                continue;
            }

            var textRect = rect with { left = rect.left + padding, right = MathF.Max(rect.left + padding, rect.right - padding) };
            var name = part.Index < 0 ? _overflowText : _crumbs[part.Index].Name;
            ChromeResources.DrawText(context, name, resources.CaptionFormat, textRect, resources.TextBrush);
        }
    }

    private void MeasureCrumbs(ChromeResources resources)
    {
        if (_crumbResources == resources && _crumbWidths.Length == _crumbs.Count)
            return;

        _crumbWidths = new float[_crumbs.Count];
        for (var i = 0; i < _crumbs.Count; i++)
        {
            _crumbWidths[i] = MeasureCaption(resources, _crumbs[i].Name);
        }
        _overflowWidth = MeasureCaption(resources, _overflowText);
        _crumbResources = resources;
    }

    private static float MeasureCaption(ChromeResources resources, string text)
    {
        using var layout = resources.Factory.CreateTextLayout(resources.CaptionFormat, text);
        layout.Object.GetMetrics(out var metrics).ThrowOnError();
        return MathF.Ceiling(metrics.widthIncludingTrailingWhitespace);
    }

    private int CrumbPartAt(float x, float y)
    {
        for (var i = 0; i < _crumbParts.Count; i++)
        {
            var rect = _crumbParts[i].Rect;
            if (x >= rect.left && x < rect.right && y >= rect.top && y < rect.bottom)
                return i;
        }
        return -1;
    }

    private bool SetHotCrumbPart(int part)
    {
        if (part == _hotCrumbPart)
            return false;

        _hotCrumbPart = part;
        _crumbHover = default;
        return true;
    }

    private readonly record struct CrumbPart(int Index, bool IsChevron, D2D_RECT_F Rect);

    private float MeasureStatus(ChromeResources resources)
    {
        if (Status.Length == 0)
            return 0;

        if (_measuredStatus == Status && _measuredResources == resources)
            return _statusWidth;

        using var layout = resources.Factory.CreateTextLayout(resources.CaptionFormat, Status);
        layout.Object.GetMetrics(out var metrics).ThrowOnError();
        _measuredStatus = Status;
        _measuredResources = resources;
        _statusWidth = MathF.Ceiling(metrics.widthIncludingTrailingWhitespace);
        return _statusWidth;
    }

    private void DrawGlyphButton(IComObject<ID2D1DeviceContext> context, ChromeResources resources, ref HoverAnimation hover, CaptionButton button, char glyph, float left, float width)
    {
        var enabled = IsEnabled(button);
        var inset = _inset * _scale;
        var rect = new D2D_RECT_F { left = left + inset, top = Bounds.top + inset, right = left + width * _scale - inset, bottom = Bounds.bottom - inset };

        // a button that gets disabled fades back out rather than dropping its highlight at once.
        if (hover.Advance(enabled && HotButton == button, resources.ElapsedSeconds))
        {
            resources.Animating = true;
        }

        // a toggle that is on keeps the selection color behind it, and so does the shield once the process is elevated.
        var on = button == CaptionButton.Hidden && ShowHidden || button == CaptionButton.Elevate && IsElevated;
        if (on)
        {
            context.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = rect, radiusX = _radius * _scale, radiusY = _radius * _scale }, resources.SelectionBrush.Object);
        }

        resources.FillHover(context, rect, hover.Opacity, _radius * _scale);
        ReadOnlySpan<char> text = [glyph];
        ChromeResources.DrawText(context, text, resources.GlyphFormat, rect, enabled || on ? resources.TextBrush : resources.DisabledTextBrush);
    }

    private void DrawWindowButton(IComObject<ID2D1DeviceContext> context, ChromeResources resources, int button, float left, float width)
    {
        var rect = new D2D_RECT_F { left = left, top = Bounds.top, right = left + width, bottom = Bounds.bottom };
        ref var hover = ref button == HitMinimize ? ref _minimizeHover : ref button == HitMaximize ? ref _maximizeHover : ref _closeHover;
        if (hover.Advance(HotWindowButton == button, resources.ElapsedSeconds))
        {
            resources.Animating = true;
        }

        // close keeps a color of its own, it fades to red rather than to the ordinary hover.
        ChromeResources.FillFaded(context, button == HitClose ? resources.BadBrush : resources.HoverBrush, rect, hover.Opacity);

        var brush = resources.TextBrush.Object;
        var half = _glyphSize * _scale / 2;
        var centerX = MathF.Round((rect.left + rect.right) / 2) + 0.5f;
        var centerY = MathF.Round((rect.top + rect.bottom) / 2) + 0.5f;
        switch (button)
        {
            case HitMinimize:
                context.Object.DrawLine(new D2D_POINT_2F(centerX - half, centerY), new D2D_POINT_2F(centerX + half, centerY), brush, 1, null);
                break;

            case HitMaximize:
                var square = new D2D_RECT_F { left = centerX - half, top = centerY - half, right = centerX + half, bottom = centerY + half };
                if (!IsMaximized)
                {
                    context.Object.DrawRectangle(square, brush, 1, null);
                    break;
                }

                // restore is a smaller square in front, and only the top and right edges of the one behind it.
                var offset = MathF.Round(2 * _scale);
                context.Object.DrawRectangle(new D2D_RECT_F { left = square.left, top = square.top + offset, right = square.right - offset, bottom = square.bottom }, brush, 1, null);
                context.Object.DrawLine(new D2D_POINT_2F(square.left + offset, square.top), new D2D_POINT_2F(square.right, square.top), brush, 1, null);
                context.Object.DrawLine(new D2D_POINT_2F(square.right, square.top), new D2D_POINT_2F(square.right, square.bottom - offset), brush, 1, null);
                break;

            default:
                context.Object.DrawLine(new D2D_POINT_2F(centerX - half, centerY - half), new D2D_POINT_2F(centerX + half, centerY + half), brush, 1, null);
                context.Object.DrawLine(new D2D_POINT_2F(centerX + half, centerY - half), new D2D_POINT_2F(centerX - half, centerY + half), brush, 1, null);
                break;
        }
    }
}
