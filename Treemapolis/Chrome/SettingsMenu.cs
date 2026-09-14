namespace Treemapolis.Chrome;

// the menu behind the gear, drawn with the rest of the chrome rather than as a popup window, so it follows the same theme and scale.
public sealed class SettingsMenu : Control
{
    private const float _rowHeight = 26;
    private const float _separatorHeight = 7;
    private const float _padding = 10;
    private const float _gap = 18;
    private const float _minimumWidth = 210;
    private const float _maximumWidth = 460;
    private const float _shadow = 1;
    private const float _radius = 6;
    private const float _wheelRows = 3;
    private const float _wheelDelta = 120;
    private const float _sliderWidth = 140;
    private const float _sliderHeight = 3;
    private const float _thumbRadius = 5;

    private IReadOnlyList<MenuEntry> _entries = [];
    private IReadOnlyList<MenuEntry> _children = [];
    private MenuEntry? _openParent;
    private RowHover _hover = new();
    private RowHover _childHover = new();
    private float _scale = 1;
    private float _averageCharacter = 7;
    private float _scrollY;
    private float _childScrollY;
    private D2D_RECT_F _frame;
    private MenuEntry? _dragging;

    public bool IsOpen { get; private set; }
    public D2D_RECT_F ChildBounds { get; private set; }
    public Action? Changed { get; set; }

    // it takes input only while it is up, and then all of it, being over everything else.
    public override bool IsInteractive => IsOpen;
    public override bool IsModal => IsOpen;
    public override bool IsCapturing => _dragging != null;
    public override bool Contains(float x, float y) => base.Contains(x, y) || (_children.Count > 0 && IsInside(ChildBounds, x, y));

    // right aligned under the anchor, the gear, and kept inside the frame.
    public void Open(IReadOnlyList<MenuEntry> entries, in D2D_RECT_F anchor, in D2D_RECT_F frame, ChromeResources resources)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(resources);
        _entries = entries;
        _scale = resources.Scale;
        _averageCharacter = resources.CaptionCharacterWidth;
        _frame = frame;
        _scrollY = 0;
        IsOpen = true;
        CloseSubmenu();
        _hover.Reset();

        var width = Math.Clamp(WidthOf(entries), _minimumWidth * _scale, _maximumWidth * _scale);
        var height = HeightOf(entries);
        var left = MathF.Max(MathF.Min(anchor.right - width, frame.right - width - _padding * _scale), frame.left + _padding * _scale);
        Bounds = new D2D_RECT_F { left = left, top = anchor.bottom, right = left + width, bottom = MathF.Min(anchor.bottom + height, frame.bottom) };
    }

    public void Close()
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        _dragging = null;
        CloseSubmenu();
        _hover.Reset();
    }

    // for a command whose work finishes after the click did.
    public void Refresh() => RefreshSubmenu();

    public override bool OnWheel(float x, float y, int delta)
    {
        var distance = delta / _wheelDelta * _wheelRows * _rowHeight * _scale;
        if (_children.Count > 0 && IsInside(ChildBounds, x, y))
        {
            _childScrollY = Math.Clamp(_childScrollY - distance, 0, MaximumScroll(_children, ChildBounds));
            _childHover.MoveTo(IndexAt(_children, ChildBounds, x, y));
            return true;
        }

        if (IsInside(Bounds, x, y))
        {
            _scrollY = Math.Clamp(_scrollY - distance, 0, MaximumScroll(_entries, Bounds));
            _hover.MoveTo(IndexAt(_entries, Bounds, x, y));
            return true;
        }
        return false;
    }

    public override bool OnMouseMove(float x, float y)
    {
        // once a slider is grabbed only x matters, wherever the pointer wanders.
        if (_dragging != null)
        {
            DragTo(_dragging, x);
            Changed?.Invoke();
            return true;
        }

        var child = _children.Count > 0 ? IndexAt(_children, ChildBounds, x, y) : -1;
        var index = child >= 0 ? _hover.Index : IndexAt(_entries, Bounds, x, y);
        var changed = _hover.MoveTo(index) | _childHover.MoveTo(child);

        // moving onto another row closes whatever submenu was open, the way a menu behaves.
        if (child < 0 && index >= 0 && !ReferenceEquals(_entries[index], _openParent))
        {
            var entry = _entries[index];
            if (entry.Kind == MenuEntryKind.Submenu)
            {
                OpenSubmenu(entry, index);
            }
            else
            {
                CloseSubmenu();
            }
            changed = true;
        }
        return changed || Contains(x, y);
    }

    public override bool OnMouseDown(float x, float y)
    {
        if (_children.Count > 0)
        {
            var childIndex = IndexAt(_children, ChildBounds, x, y);
            if (childIndex >= 0)
            {
                Activate(_children[childIndex], x);
                return true;
            }
        }

        var index = IndexAt(_entries, Bounds, x, y);
        if (index >= 0)
        {
            Activate(_entries[index], x);
            return true;
        }

        // a click anywhere else dismisses it, a disabled row swallows the click rather than closing the menu under it.
        if (!Contains(x, y))
        {
            Close();
        }
        return true;
    }

    public override bool OnMouseUp()
    {
        if (_dragging == null)
            return false;

        _dragging = null;
        return true;
    }

    public override bool OnKeyDown(VIRTUAL_KEY key)
    {
        if (key != VIRTUAL_KEY.VK_ESCAPE)
            return false;

        Close();
        return true;
    }

    public void Render(IComObject<ID2D1DeviceContext> context, ChromeResources resources)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resources);
        if (!IsOpen)
            return;

        if (_hover.Advance(resources.ElapsedSeconds) | _childHover.Advance(resources.ElapsedSeconds))
        {
            resources.Animating = true;
        }

        RenderList(context, resources, _entries, Bounds, _hover);
        if (_children.Count > 0)
        {
            RenderList(context, resources, _children, ChildBounds, _childHover);
        }
    }

    private void Activate(MenuEntry entry, float x)
    {
        if (entry.Kind == MenuEntryKind.Submenu)
            return;

        if (entry.Kind == MenuEntryKind.Slider)
        {
            _dragging = entry;
            DragTo(entry, x);
            Changed?.Invoke();
            return;
        }

        entry.Invoked?.Invoke();
        Changed?.Invoke();
        if (entry.ClosesMenu)
        {
            Close();
            return;
        }
        RefreshSubmenu();
    }

    private void OpenSubmenu(MenuEntry entry, int index)
    {
        var children = entry.Children?.Invoke() ?? [];
        if (children.Count == 0)
        {
            CloseSubmenu();
            return;
        }

        _openParent = entry;
        _children = children;
        _childHover.Reset();
        _childScrollY = 0;

        var top = Bounds.top + _padding * _scale - _scrollY;
        for (var i = 0; i < index; i++)
        {
            top += HeightOf(_entries[i]);
        }

        // opened to the left, the gear sits at the right edge of the window.
        var width = Math.Clamp(WidthOf(children), _minimumWidth * _scale, _maximumWidth * _scale);
        var height = MathF.Min(HeightOf(children), _frame.bottom - _frame.top);
        top = MathF.Max(MathF.Min(top, _frame.bottom - height), _frame.top);
        ChildBounds = new D2D_RECT_F { left = Bounds.left - width, top = top, right = Bounds.left, bottom = top + height };
    }

    private void CloseSubmenu()
    {
        _openParent = null;
        _children = [];
        _childHover.Reset();
        _childScrollY = 0;
        ChildBounds = default;
    }

    private void RefreshSubmenu()
    {
        if (_openParent == null)
            return;

        _children = _openParent.Children?.Invoke() ?? [];
        if (_children.Count == 0)
        {
            CloseSubmenu();
            return;
        }

        var height = MathF.Min(HeightOf(_children), _frame.bottom - _frame.top);
        var top = MathF.Max(_frame.top, MathF.Min(ChildBounds.top, _frame.bottom - height));
        ChildBounds = new D2D_RECT_F { left = ChildBounds.left, top = top, right = ChildBounds.right, bottom = top + height };
        _childScrollY = Math.Clamp(_childScrollY, 0, MaximumScroll(_children, ChildBounds));
    }

    private void RenderList(IComObject<ID2D1DeviceContext> context, ChromeResources resources, IReadOnlyList<MenuEntry> entries, D2D_RECT_F bounds, RowHover hover)
    {
        var native = context.Object;
        var radius = _radius * _scale;
        var shadow = _shadow * _scale;
        var shadowRect = new D2D_RECT_F { left = bounds.left + shadow, top = bounds.top + shadow, right = bounds.right + shadow, bottom = bounds.bottom + shadow };
        native.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = shadowRect, radiusX = radius, radiusY = radius }, resources.MenuShadowBrush.Object);
        native.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = bounds, radiusX = radius, radiusY = radius }, resources.MenuBackgroundBrush.Object);
        native.DrawRoundedRectangle(new D2D1_ROUNDED_RECT { rect = bounds, radiusX = radius, radiusY = radius }, resources.LineBrush.Object, 1, null);

        var padding = _padding * _scale;
        native.PushAxisAlignedClip(bounds, D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_ALIASED);
        var top = bounds.top + padding - ScrollOf(entries);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var height = HeightOf(entry);
            var row = new D2D_RECT_F { left = bounds.left, top = top, right = bounds.right, bottom = top + height };
            top += height;
            if (entry.Kind == MenuEntryKind.Separator)
            {
                var middle = MathF.Round((row.top + row.bottom) / 2) - 0.5f;
                native.DrawLine(new D2D_POINT_2F(row.left + padding, middle), new D2D_POINT_2F(row.right - padding, middle), resources.LineBrush.Object, 1, null);
                continue;
            }

            resources.FillHover(context, row, hover.OpacityOf(i));
            var labelRect = new D2D_RECT_F { left = row.left + padding, top = row.top, right = row.right - padding - ReserveOf(entry), bottom = row.bottom };
            ChromeResources.DrawText(context, entry.Label, resources.CaptionFormat, labelRect, entry.IsInteractive ? resources.TextBrush : resources.DimTextBrush);

            var end = new D2D_RECT_F { left = row.right - (_padding + _gap) * _scale, top = row.top, right = row.right - padding, bottom = row.bottom };
            var valueRect = new D2D_RECT_F { left = row.left + padding, top = row.top, right = row.right - padding - (entry.Kind == MenuEntryKind.Submenu ? _gap * _scale : 0), bottom = row.bottom };
            switch (entry.Kind)
            {
                case MenuEntryKind.Toggle:
                    if (entry.Checked?.Invoke() == true)
                    {
                        ReadOnlySpan<char> check = [resources.Glyphs.Check];
                        ChromeResources.DrawText(context, check, resources.GlyphFormat, end, resources.GoodBrush);
                    }
                    break;

                case MenuEntryKind.Submenu:
                    ChromeResources.DrawText(context, entry.Value?.Invoke(), resources.CaptionRightFormat, valueRect, resources.DimTextBrush);
                    ReadOnlySpan<char> arrow = [resources.Glyphs.Submenu];
                    ChromeResources.DrawText(context, arrow, resources.GlyphFormat, end, resources.DimTextBrush);
                    break;

                case MenuEntryKind.Slider:
                    RenderSlider(context, resources, entry, row);
                    break;

                default:
                    ChromeResources.DrawText(context, entry.Value?.Invoke(), resources.CaptionRightFormat, valueRect, resources.DimTextBrush);
                    break;
            }
        }
        native.PopAxisAlignedClip();
    }

    // what a row keeps to the right of its label, its value and its glyph or its slider.
    private float ReserveOf(MenuEntry entry)
    {
        var value = (entry.Value?.Invoke().Length ?? 0) * _averageCharacter;
        return entry.Kind switch
        {
            MenuEntryKind.Slider => (_sliderWidth + _gap) * _scale + value,
            MenuEntryKind.Submenu or MenuEntryKind.Toggle => _gap * _scale + value,
            _ => value,
        };
    }

    // the track sits at the right of the row, the value is written just left of it.
    private void RenderSlider(IComObject<ID2D1DeviceContext> context, ChromeResources resources, MenuEntry entry, in D2D_RECT_F row)
    {
        var padding = _padding * _scale;
        var right = row.right - padding;
        var left = right - _sliderWidth * _scale;
        var center = MathF.Round((row.top + row.bottom) / 2);
        var height = _sliderHeight * _scale;
        var track = new D2D_RECT_F { left = left, top = center - height / 2, right = right, bottom = center + height / 2 };
        context.Object.FillRoundedRectangle(new D2D1_ROUNDED_RECT { rect = track, radiusX = height / 2, radiusY = height / 2 }, resources.TrackBrush.Object);

        var span = entry.Maximum - entry.Minimum;
        var ratio = span <= 0 ? 0 : Math.Clamp(((entry.Number?.Invoke() ?? 0) - entry.Minimum) / span, 0, 1);
        var thumbRadius = _thumbRadius * _scale;
        var thumb = new D2D1_ELLIPSE { point = new D2D_POINT_2F(left + (float)((right - left) * ratio), center), radiusX = thumbRadius, radiusY = thumbRadius };
        context.Object.FillEllipse(thumb, resources.ThumbBrush.Object);

        var valueRect = new D2D_RECT_F { left = row.left + padding, top = row.top, right = left - _gap * _scale * 0.5f, bottom = row.bottom };
        ChromeResources.DrawText(context, entry.Value?.Invoke(), resources.CaptionRightFormat, valueRect, resources.DimTextBrush);
    }

    private void DragTo(MenuEntry entry, float x)
    {
        if (entry.SetNumber == null)
            return;

        var bounds = _children.Contains(entry) ? ChildBounds : Bounds;
        var right = bounds.right - _padding * _scale;
        var left = right - _sliderWidth * _scale;
        var ratio = Math.Clamp((x - left) / (right - left), 0, 1);
        var value = entry.Minimum + (entry.Maximum - entry.Minimum) * ratio;
        if (entry.Step > 0)
        {
            value = Math.Round(value / entry.Step) * entry.Step;
        }
        entry.SetNumber(Math.Clamp(value, entry.Minimum, entry.Maximum));
    }

    // measured from the average character width rather than laid out, a menu is a dozen short strings.
    private float WidthOf(IReadOnlyList<MenuEntry> entries)
    {
        var widest = 0f;
        foreach (var entry in entries)
        {
            widest = MathF.Max(widest, (entry.Label.Length + 1) * _averageCharacter + ReserveOf(entry));
        }
        return widest + _padding * 2 * _scale;
    }

    private float HeightOf(MenuEntry entry) => (entry.Kind == MenuEntryKind.Separator ? _separatorHeight : _rowHeight) * _scale;

    private float HeightOf(IReadOnlyList<MenuEntry> entries)
    {
        var height = _padding * _scale * 2;
        foreach (var entry in entries)
        {
            height += HeightOf(entry);
        }
        return height;
    }

    private float ScrollOf(IReadOnlyList<MenuEntry> entries) => ReferenceEquals(entries, _children) ? _childScrollY : _scrollY;
    private float MaximumScroll(IReadOnlyList<MenuEntry> entries, in D2D_RECT_F bounds) => MathF.Max(0, HeightOf(entries) - (bounds.bottom - bounds.top));

    private int IndexAt(IReadOnlyList<MenuEntry> entries, in D2D_RECT_F bounds, float x, float y)
    {
        if (!IsInside(bounds, x, y))
            return -1;

        var top = bounds.top + _padding * _scale - ScrollOf(entries);
        for (var i = 0; i < entries.Count; i++)
        {
            var height = HeightOf(entries[i]);
            if (y >= top && y < top + height)
                return entries[i].IsInteractive ? i : -1;

            top += height;
        }
        return -1;
    }

    private static bool IsInside(in D2D_RECT_F rect, float x, float y) => x >= rect.left && x < rect.right && y >= rect.top && y < rect.bottom;
}
