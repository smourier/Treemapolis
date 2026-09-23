namespace Treemapolis.Chrome;

// a list of folders to pick from with a filter typed over it, the subfolders under a breadcrumb or every folder of the tree.
// the rows are found away from the UI thread, a search over millions of names should never hold a keystroke back,
// and a newer filter makes whatever an older one still finds useless.
public sealed class FolderPicker : Control
{
    private const float _filterHeight = 36;
    private const float _rowHeight = 42;
    private const float _padding = 10;
    private const float _gap = 12;
    private const float _sizeWidth = 90;
    private const float _barHeight = 2;
    private const float _barOpacity = 0.7f;
    private const float _caretWidth = 1;
    private const float _caretInset = 10;
    private const int _maximumVisibleRows = 12;
    private const float _wheelRows = 3;
    private const float _wheelDelta = 120;

    private IReadOnlyList<FolderRow> _rows = [];
    private Func<string, CancellationToken, IReadOnlyList<FolderRow>>? _search;
    private CancellationTokenSource? _searching;
    private string _placeholder = string.Empty;
    private D2D_RECT_F _anchor;
    private D2D_RECT_F _frame;
    private bool _centered;
    private float _width;
    private float _scale = 1;
    private float _scroll;
    private int _caret;
    private bool _busy;
    private RowHover _hover = new();
    private IComObject<IDWriteTextLayout>? _filterLayout;
    private string? _filterLayoutText;
    private ChromeResources? _filterLayoutResources;

    public bool IsOpen { get; private set; }
    public string Filter { get; private set; } = string.Empty;
    public IReadOnlyList<FolderRow> Rows => _rows;
    public int HotIndex => _hover.Index;

    // the folder picked, the folder under the pointer or the keyboard, and a change that needs a chrome frame.
    public Action<FolderRow>? Chosen { get; set; }
    public Action<int>? Previewed { get; set; }
    public Action? Updated { get; set; }

    public override bool IsInteractive => IsOpen;
    public override bool IsModal => IsOpen;

    // under the anchor, from its left edge or centered on it, and kept inside the frame.
    public void Open(string placeholder, in D2D_RECT_F anchor, bool centered, float width, in D2D_RECT_F frame, float scale, Func<string, CancellationToken, IReadOnlyList<FolderRow>> search)
    {
        ArgumentNullException.ThrowIfNull(placeholder);
        ArgumentNullException.ThrowIfNull(search);
        Close();
        _placeholder = placeholder;
        _anchor = anchor;
        _centered = centered;
        _width = width;
        _frame = frame;
        _scale = scale;
        _search = search;
        Filter = string.Empty;
        _caret = 0;
        _rows = [];
        _scroll = 0;
        _hover.Reset();
        IsOpen = true;
        UpdateBounds();
        Search();
    }

    public void Close()
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        _searching?.Cancel();
        _searching = null;
        _rows = [];
        _busy = false;
        _hover.Reset();
        DisposeFilterLayout();
        Previewed?.Invoke(Entry.None);
    }

    public bool OnChar(char character)
    {
        if (!IsOpen || char.IsControl(character))
            return false;

        Filter = Filter.Insert(_caret, character.ToString());
        _caret++;
        Search();
        return true;
    }

    // the caret moves by character, or by word with Ctrl the way an edit box does, and Backspace and Delete remove a word with Ctrl too.
    private bool EditFilter(VIRTUAL_KEY key)
    {
        var byWord = Functions.GetKeyState((int)VIRTUAL_KEY.VK_CONTROL) < 0;
        switch (key)
        {
            case VIRTUAL_KEY.VK_LEFT:
                _caret = byWord ? PreviousWordStart(_caret) : Math.Max(0, _caret - 1);
                return true;

            case VIRTUAL_KEY.VK_RIGHT:
                _caret = byWord ? NextWordStart(_caret) : Math.Min(Filter.Length, _caret + 1);
                return true;

            case VIRTUAL_KEY.VK_HOME:
                _caret = 0;
                return true;

            case VIRTUAL_KEY.VK_END:
                _caret = Filter.Length;
                return true;

            case VIRTUAL_KEY.VK_BACK:
                if (_caret > 0)
                {
                    var start = byWord ? PreviousWordStart(_caret) : _caret - 1;
                    Filter = Filter.Remove(start, _caret - start);
                    _caret = start;
                    Search();
                }
                return true;

            case VIRTUAL_KEY.VK_DELETE:
                if (_caret < Filter.Length)
                {
                    var end = byWord ? NextWordStart(_caret) : _caret + 1;
                    Filter = Filter.Remove(_caret, end - _caret);
                    Search();
                }
                return true;
        }
        return false;
    }

    // a word is a run of letters and digits, anything else only separates words.
    private int PreviousWordStart(int position)
    {
        while (position > 0 && !char.IsLetterOrDigit(Filter[position - 1]))
        {
            position--;
        }

        while (position > 0 && char.IsLetterOrDigit(Filter[position - 1]))
        {
            position--;
        }
        return position;
    }

    private int NextWordStart(int position)
    {
        while (position < Filter.Length && char.IsLetterOrDigit(Filter[position]))
        {
            position++;
        }

        while (position < Filter.Length && !char.IsLetterOrDigit(Filter[position]))
        {
            position++;
        }
        return position;
    }

    public override bool OnKeyDown(VIRTUAL_KEY key)
    {
        if (!IsOpen)
            return false;

        if (EditFilter(key))
            return true;

        switch (key)
        {
            case VIRTUAL_KEY.VK_ESCAPE:
                Close();
                return true;

            case VIRTUAL_KEY.VK_RETURN:
                var index = _hover.Index >= 0 ? _hover.Index : 0;
                if (index < _rows.Count)
                {
                    Choose(index);
                }
                return true;

            case VIRTUAL_KEY.VK_DOWN:
                MoveHot(1);
                return true;

            case VIRTUAL_KEY.VK_UP:
                MoveHot(-1);
                return true;

            case VIRTUAL_KEY.VK_NEXT:
                MoveHot(_maximumVisibleRows);
                return true;

            case VIRTUAL_KEY.VK_PRIOR:
                MoveHot(-_maximumVisibleRows);
                return true;
        }

        // whatever else is pressed, the letters come as characters, is not for the map behind the list.
        return true;
    }

    public override bool OnMouseMove(float x, float y)
    {
        var index = IndexAt(x, y);
        if (index < 0)
            return Contains(x, y);

        return SetHot(index);
    }

    public override bool OnMouseDown(float x, float y)
    {
        var index = IndexAt(x, y);
        if (index >= 0)
        {
            Choose(index);
            return true;
        }

        if (!Contains(x, y))
        {
            Close();
        }
        return true;
    }

    public override bool OnWheel(float x, float y, int delta)
    {
        var distance = delta / _wheelDelta * _wheelRows * _rowHeight * _scale;
        _scroll = Math.Clamp(_scroll - distance, 0, MaximumScroll);
        SetHot(IndexAt(x, y));
        return true;
    }

    public void Render(IComObject<ID2D1DeviceContext> context, ChromeResources resources)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resources);
        if (!IsOpen)
            return;

        if (_hover.Advance(resources.ElapsedSeconds))
        {
            resources.Animating = true;
        }

        var native = context.Object;
        resources.DrawMenuPanel(context, Bounds);
        var padding = _padding * _scale;
        var filterRect = new D2D_RECT_F { left = Bounds.left + padding, top = Bounds.top, right = Bounds.right - padding, bottom = Bounds.top + _filterHeight * _scale };
        if (Filter.Length == 0)
        {
            ChromeResources.DrawText(context, _placeholder, resources.CaptionFormat, filterRect, resources.DimTextBrush);
        }
        else
        {
            ChromeResources.DrawText(context, Filter, resources.CaptionFormat, filterRect, resources.TextBrush);
        }

        var caretX = MathF.Round(filterRect.left + CaretOffset(resources)) + 0.5f;
        var inset = _caretInset * _scale;
        native.DrawLine(new D2D_POINT_2F(caretX, filterRect.top + inset), new D2D_POINT_2F(caretX, filterRect.bottom - inset), resources.TextBrush.Object, _caretWidth * _scale, null);
        native.DrawLine(new D2D_POINT_2F(Bounds.left, filterRect.bottom - 0.5f), new D2D_POINT_2F(Bounds.right, filterRect.bottom - 0.5f), resources.LineBrush.Object, 1, null);

        var list = ListBounds;
        if (_rows.Count == 0)
        {
            ChromeResources.DrawText(context, _busy ? Res.PickerSearching : Res.PickerNothing, resources.CaptionCenterFormat, list, resources.DimTextBrush);
            return;
        }

        native.PushAxisAlignedClip(list, D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_ALIASED);
        var rowHeight = _rowHeight * _scale;
        var first = Math.Max(0, (int)(_scroll / rowHeight));
        var sizeWidth = _sizeWidth * _scale;
        var accent = resources.ThumbBrush;
        for (var i = first; i < _rows.Count; i++)
        {
            var top = list.top + i * rowHeight - _scroll;
            if (top >= list.bottom)
                break;

            var row = _rows[i];
            var rect = new D2D_RECT_F { left = list.left, top = top, right = list.right, bottom = top + rowHeight };
            resources.FillHover(context, rect, _hover.OpacityOf(i));

            var middle = top + rowHeight / 2;
            var nameRect = new D2D_RECT_F { left = rect.left + padding, top = top, right = rect.right - padding - sizeWidth - _gap * _scale, bottom = middle + 2 * _scale };
            var detailRect = nameRect with { top = middle - 3 * _scale, bottom = rect.bottom - 5 * _scale };
            var sizeRect = new D2D_RECT_F { left = rect.right - padding - sizeWidth, top = top, right = rect.right - padding, bottom = middle + 2 * _scale };
            ChromeResources.DrawText(context, row.Name, resources.CaptionFormat, nameRect, row.IsCurrent ? accent : resources.TextBrush);
            ChromeResources.DrawText(context, row.Detail, resources.CaptionFormat, detailRect, resources.DimTextBrush);
            ChromeResources.DrawText(context, row.Size, resources.CaptionRightFormat, sizeRect, resources.DimTextBrush);

            // how big it is next to the largest one of the list, a thin bar along the bottom of the row.
            var barRect = new D2D_RECT_F { left = rect.left + padding, top = rect.bottom - _barHeight * _scale - 1, right = rect.left + padding + (rect.right - rect.left - 2 * padding) * Math.Clamp(row.Share, 0, 1), bottom = rect.bottom - 1 };
            ChromeResources.FillFaded(context, accent, barRect, _barOpacity);
        }
        native.PopAxisAlignedClip();
    }

    private D2D_RECT_F ListBounds => new() { left = Bounds.left, top = Bounds.top + _filterHeight * _scale, right = Bounds.right, bottom = Bounds.bottom - _padding * _scale / 2 };
    private float MaximumScroll => MathF.Max(0, _rows.Count * _rowHeight * _scale - (ListBounds.bottom - ListBounds.top));

    private void Choose(int index)
    {
        var row = _rows[index];
        Close();
        Chosen?.Invoke(row);
    }

    private void MoveHot(int delta)
    {
        if (_rows.Count == 0)
            return;

        var index = Math.Clamp((_hover.Index < 0 ? -1 : _hover.Index) + delta, 0, _rows.Count - 1);
        SetHot(index);

        // the row the keyboard moved to is scrolled into view.
        var rowHeight = _rowHeight * _scale;
        var visible = ListBounds.bottom - ListBounds.top;
        _scroll = MathF.Min(MathF.Max(_scroll, index * rowHeight + rowHeight - visible), index * rowHeight);
        _scroll = Math.Clamp(_scroll, 0, MaximumScroll);
    }

    private bool SetHot(int index)
    {
        if (!_hover.MoveTo(index))
            return false;

        Previewed?.Invoke(index >= 0 && index < _rows.Count ? _rows[index].Entry : Entry.None);
        return true;
    }

    private int IndexAt(float x, float y)
    {
        var list = ListBounds;
        if (x < list.left || x >= list.right || y < list.top || y >= list.bottom)
            return -1;

        var index = (int)((y - list.top + _scroll) / (_rowHeight * _scale));
        return index < _rows.Count ? index : -1;
    }

    private void UpdateBounds()
    {
        var rows = Math.Clamp(_rows.Count, 1, _maximumVisibleRows);
        var width = MathF.Min(_width, _frame.right - _frame.left - 2 * _padding * _scale);
        var height = MathF.Min((_filterHeight + rows * _rowHeight) * _scale + _padding * _scale / 2, _frame.bottom - _anchor.bottom - _padding * _scale);
        var left = _centered ? (_anchor.left + _anchor.right - width) / 2 : _anchor.left;
        left = Math.Clamp(left, _frame.left + _padding * _scale, MathF.Max(_frame.left + _padding * _scale, _frame.right - width - _padding * _scale));
        Bounds = new D2D_RECT_F { left = left, top = _anchor.bottom, right = left + width, bottom = _anchor.bottom + height };
    }

    private async void Search()
    {
        _searching?.Cancel();
        var cancellation = new CancellationTokenSource();
        _searching = cancellation;
        var search = _search;
        var filter = Filter;
        _busy = true;
        Updated?.Invoke();
        if (search == null)
            return;

        try
        {
            var rows = await Task.Run(() => search(filter, cancellation.Token), cancellation.Token).ConfigureAwait(true);
            if (cancellation.IsCancellationRequested || !IsOpen)
                return;

            _rows = rows;
            _busy = false;
            _scroll = 0;
            _hover.Reset();
            SetHot(rows.Count > 0 ? 0 : -1);
            UpdateBounds();
            Updated?.Invoke();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Application.TraceError($"the folders could not be listed: {ex}");
            _busy = false;
            Updated?.Invoke();
        }
    }

    // how far into the typed text the caret is, measured by DirectWrite at the leading edge of the character after it.
    private float CaretOffset(ChromeResources resources)
    {
        if (Filter.Length == 0 || _caret == 0)
            return 0;

        if (_filterLayout == null || _filterLayoutText != Filter || _filterLayoutResources != resources)
        {
            DisposeFilterLayout();
            _filterLayout = resources.Factory.CreateTextLayout(resources.CaptionFormat, Filter);
            _filterLayoutText = Filter;
            _filterLayoutResources = resources;
        }

        var position = Math.Min(_caret, Filter.Length);
        _filterLayout.HitTestTextPosition((uint)Math.Min(position, Filter.Length - 1), position >= Filter.Length, out var x, out _);
        return x;
    }

    private void DisposeFilterLayout()
    {
        _filterLayout?.Dispose();
        _filterLayout = null;
        _filterLayoutText = null;
        _filterLayoutResources = null;
    }
}
