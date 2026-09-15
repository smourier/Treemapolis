namespace Treemapolis.Chrome;

// a sunburst of folders around the pointer, each arc as wide as the share of its parent it takes on disk.
// the first ring holds the subfolders of the folder the wheel opened on, pointing at an arc grows the next ring out of it,
// so a whole path is reached in one gesture and a click on any arc goes there.
public sealed class FolderWheel : Control
{
    private const float _innerRadius = 72;
    private const float _ringWidth = 44;
    private const int _maximumRings = 5;
    private const int _maximumArcs = 64;
    private const float _minimumSweep = 0.025f;
    private const float _gap = 1.5f;
    private const float _backdropMargin = 6;
    private const float _saturation = 0.55f;
    private const float _value = 0.82f;
    private const float _valueStep = 0.08f;
    private const float _hoverValue = 0.15f;
    private const float _mergedValue = 0.42f;
    private const float _outlineWidth = 1.5f;
    private const float _textInset = 10;
    private const float _fullTurnSlack = 0.0005f;

    private readonly List<List<WheelArc>> _rings = [];
    private readonly List<int> _path = [];
    private Func<int, IReadOnlyList<int>>? _subfolders;
    private Func<int, long>? _sizeOf;
    private Func<int, FolderRow>? _describe;
    private D2D_POINT_2F _center;
    private float _scale = 1;
    private int _root = Entry.None;
    private int _described = Entry.None;
    private FolderRow _description;

    public bool IsOpen { get; private set; }
    public Action<int>? Chosen { get; set; }
    public Action<int>? Previewed { get; set; }

    public override bool IsInteractive => IsOpen;
    public override bool IsModal => IsOpen;
    public override bool Contains(float x, float y) => IsOpen;

    // the folder the pointer rests on, the deepest of the path, or the folder the wheel opened on.
    private int Focus => _path.Count > 0 ? _rings[_path.Count - 1][_path[^1]].Entry : _root;

    public void Open(D2D_POINT_2F center, int root, in D2D_RECT_F frame, float scale, Func<int, IReadOnlyList<int>> subfolders, Func<int, long> sizeOf, Func<int, FolderRow> describe)
    {
        ArgumentNullException.ThrowIfNull(subfolders);
        ArgumentNullException.ThrowIfNull(sizeOf);
        ArgumentNullException.ThrowIfNull(describe);
        _subfolders = subfolders;
        _sizeOf = sizeOf;
        _describe = describe;
        _scale = scale;
        _root = root;
        _rings.Clear();
        _path.Clear();
        _described = Entry.None;

        // kept far enough inside the frame for the first two rings to show whole.
        var reach = (_innerRadius + 2 * _ringWidth) * scale;
        _center = new D2D_POINT_2F(
            Math.Clamp(center.x, frame.left + reach, MathF.Max(frame.left + reach, frame.right - reach)),
            Math.Clamp(center.y, frame.top + reach, MathF.Max(frame.top + reach, frame.bottom - reach)));
        Bounds = new D2D_RECT_F { left = frame.left, top = frame.top, right = frame.right, bottom = frame.bottom };

        var ring = BuildRing(root, -MathF.PI / 2, MathF.Tau);
        if (ring.Count > 0)
        {
            _rings.Add(ring);
        }
        IsOpen = true;
    }

    public void Close()
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        _rings.Clear();
        _path.Clear();
        Previewed?.Invoke(Entry.None);
    }

    public override bool OnMouseMove(float x, float y)
    {
        if (!IsOpen)
            return false;

        // just beyond the rings the outer one is followed by angle, so moving straight out drills down that way.
        var (ring, arc) = ArcAt(x, y, true);
        if (ring < 0)
            return false;

        if (ring == int.MaxValue)
        {
            SetPath(0);
            return true;
        }

        if (arc < 0)
            return false;

        // pointing at an arc makes it the end of the path, the rings beyond grow out of it, a merged arc has nothing to grow.
        if (_path.Count == ring + 1 && _path[ring] == arc)
            return false;

        SetPath(ring);
        _path.Add(arc);
        var chosen = _rings[ring][arc];
        if (chosen.Entry >= 0 && ring + 1 < _maximumRings)
        {
            var next = BuildRing(chosen.Entry, chosen.Start, chosen.Sweep);
            if (next.Count > 0)
            {
                _rings.Add(next);
            }
        }
        Previewed?.Invoke(chosen.Entry);
        return true;
    }

    public override bool OnMouseDown(float x, float y)
    {
        if (!IsOpen)
            return false;

        var (ring, arc) = ArcAt(x, y, false);
        if (ring == int.MaxValue)
        {
            Choose(_root);
            return true;
        }

        if (ring >= 0 && arc >= 0 && _rings[ring][arc].Entry >= 0)
        {
            Choose(_rings[ring][arc].Entry);
            return true;
        }

        // a click anywhere else, a merged arc included, dismisses the wheel.
        Close();
        return true;
    }

    public override bool OnKeyDown(VIRTUAL_KEY key)
    {
        if (!IsOpen)
            return false;

        switch (key)
        {
            case VIRTUAL_KEY.VK_ESCAPE:
                Close();
                break;

            case VIRTUAL_KEY.VK_RETURN:
                if (Focus >= 0)
                {
                    Choose(Focus);
                }
                break;
        }
        return true;
    }

    public void Render(IComObject<ID2D1DeviceContext> context, ChromeResources resources)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resources);
        if (!IsOpen)
            return;

        // only the first ring sits on a backdrop, the deeper rings are narrow wedges that would otherwise hide the map under a wide disk.
        var native = context.Object;
        var backdrop = (_innerRadius + _ringWidth + _backdropMargin) * _scale;
        native.FillEllipse(new D2D1_ELLIPSE { point = _center, radiusX = backdrop, radiusY = backdrop }, resources.MenuShadowBrush.Object);

        using var factory = context.GetFactory()!;
        var swatch = (ID2D1SolidColorBrush)resources.SwatchBrush.Object;
        for (var ring = 0; ring < _rings.Count; ring++)
        {
            var inner = (_innerRadius + ring * _ringWidth) * _scale;
            var ringOuter = inner + _ringWidth * _scale;
            var arcs = _rings[ring];
            for (var index = 0; index < arcs.Count; index++)
            {
                var arc = arcs[index];
                var hovered = ring < _path.Count && _path[ring] == index;
                using var geometry = CreateArc(factory, _center, arc, inner + _gap * _scale, ringOuter - _gap * _scale);
                if (geometry == null)
                    continue;

                var value = arc.Entry < 0 ? _mergedValue : _value - ring * _valueStep + (hovered ? _hoverValue : 0);
                var saturation = arc.Entry < 0 ? 0 : _saturation;
                swatch.SetColor(FromHsv((arc.Start + arc.Sweep / 2 + MathF.PI / 2) / MathF.Tau, saturation, Math.Clamp(value, 0, 1)));
                native.FillGeometry(geometry.Object, swatch, null);
                if (hovered)
                {
                    native.DrawGeometry(geometry.Object, resources.TextBrush.Object, _outlineWidth * _scale, null);
                }
            }
        }

        // the middle names the folder at the end of the path, a click there goes to the folder the wheel opened on.
        var radius = _innerRadius * _scale - _gap * _scale;
        var disk = new D2D1_ELLIPSE { point = _center, radiusX = radius, radiusY = radius };
        native.FillEllipse(disk, resources.MenuBackgroundBrush.Object);
        native.DrawEllipse(disk, resources.LineBrush.Object, 1, null);

        var focus = Focus;
        if (focus >= 0 && _describe != null)
        {
            if (focus != _described)
            {
                _described = focus;
                _description = _describe(focus);
            }

            var inset = _textInset * _scale;
            var textWidth = radius * 2 - 2 * inset;
            var lineHeight = radius * 0.5f;
            var nameRect = new D2D_RECT_F { left = _center.x - textWidth / 2, top = _center.y - lineHeight, right = _center.x + textWidth / 2, bottom = _center.y };
            var sizeRect = nameRect with { top = _center.y, bottom = _center.y + lineHeight / 2 };
            var detailRect = nameRect with { top = sizeRect.bottom, bottom = _center.y + lineHeight };
            ChromeResources.DrawText(context, _description.Name, resources.CaptionCenterFormat, nameRect, resources.TextBrush);
            ChromeResources.DrawText(context, _description.Size, resources.CaptionCenterFormat, sizeRect, resources.TextBrush);
            ChromeResources.DrawText(context, _description.Detail, resources.CaptionCenterFormat, detailRect, resources.DimTextBrush);
        }
    }

    private float OuterRadius => (_innerRadius + MathF.Max(1, _rings.Count) * _ringWidth) * _scale;

    private float Distance(float x, float y) => MathF.Sqrt((x - _center.x) * (x - _center.x) + (y - _center.y) * (y - _center.y));

    private void Choose(int entry)
    {
        Close();
        Chosen?.Invoke(entry);
    }

    // keeps the first rings of the path, the ones beyond it go.
    private void SetPath(int length)
    {
        if (_path.Count > length)
        {
            _path.RemoveRange(length, _path.Count - length);
        }

        if (_rings.Count > length + 1)
        {
            _rings.RemoveRange(length + 1, _rings.Count - length - 1);
        }

        if (length == 0)
        {
            Previewed?.Invoke(_root);
        }
    }

    // the ring and the arc under a point, the middle as ring int.MaxValue, a ring with no arc there as arc -1,
    // beyond the rings as ring -1 unless the outer ring is followed out there.
    private (int Ring, int Arc) ArcAt(float x, float y, bool followOuterRing)
    {
        var distance = Distance(x, y);
        if (distance < _innerRadius * _scale)
            return (int.MaxValue, -1);

        if (_rings.Count == 0)
            return (-1, -1);

        var ring = (int)((distance / _scale - _innerRadius) / _ringWidth);
        // only the band where the next ring would grow follows the outer ring, farther out nothing changes.
        if (ring >= _rings.Count)
        {
            if (!followOuterRing || ring > _rings.Count)
                return (-1, -1);

            ring = _rings.Count - 1;
        }

        var angle = MathF.Atan2(y - _center.y, x - _center.x);
        var arcs = _rings[ring];
        for (var i = 0; i < arcs.Count; i++)
        {
            var offset = (angle - arcs[i].Start) % MathF.Tau;
            if (offset < 0)
            {
                offset += MathF.Tau;
            }

            if (offset < arcs[i].Sweep)
                return (ring, i);
        }
        return (ring, -1);
    }

    // the subfolders spread over the sweep of their parent in proportion to their size, the ones too thin to see merged into one arc at the end.
    private List<WheelArc> BuildRing(int parent, float start, float sweep)
    {
        var arcs = new List<WheelArc>();
        if (_subfolders == null || _sizeOf == null)
            return arcs;

        var folders = _subfolders(parent);
        if (folders.Count == 0)
            return arcs;

        long total = 0;
        foreach (var folder in folders)
        {
            total += Math.Max(0, _sizeOf(folder));
        }

        var angle = start;
        var end = start + sweep;
        foreach (var folder in folders)
        {
            var share = total > 0 ? (float)(Math.Max(0, _sizeOf(folder)) / (double)total) : 1f / folders.Count;
            var folderSweep = sweep * share;
            if (folderSweep < _minimumSweep || arcs.Count == _maximumArcs - 1)
                break;

            arcs.Add(new WheelArc(folder, angle, folderSweep));
            angle += folderSweep;
        }

        if (end - angle > _minimumSweep / 2)
        {
            arcs.Add(new WheelArc(Entry.None, angle, end - angle));
        }
        return arcs;
    }

    private static IComObject<ID2D1PathGeometry>? CreateArc(IComObject<ID2D1Factory> factory, D2D_POINT_2F center, WheelArc arc, float inner, float outer)
    {
        var sweep = MathF.Min(arc.Sweep, MathF.Tau - _fullTurnSlack);
        if (sweep <= 0 || outer <= inner)
            return null;

        // a gap as wide at both radii, so neighbours are parted by the same line all along.
        var gapAngle = MathF.Min(sweep / 3, _gap / outer);
        var start = arc.Start + gapAngle / 2;
        var end = arc.Start + sweep - gapAngle / 2;
        var large = end - start > MathF.PI ? D2D1_ARC_SIZE.D2D1_ARC_SIZE_LARGE : D2D1_ARC_SIZE.D2D1_ARC_SIZE_SMALL;

        var geometry = factory.CreatePathGeometry();
        using var sink = geometry.Open<ID2D1GeometrySink>();
        var native = sink.Object;
        native.BeginFigure(Polar(start, outer), D2D1_FIGURE_BEGIN.D2D1_FIGURE_BEGIN_FILLED);
        native.AddArc(Polar(end, outer), new D2D_SIZE_F(outer, outer), 0, D2D1_SWEEP_DIRECTION.D2D1_SWEEP_DIRECTION_CLOCKWISE, large);
        native.AddLine(Polar(end, inner));
        native.AddArc(Polar(start, inner), new D2D_SIZE_F(inner, inner), 0, D2D1_SWEEP_DIRECTION.D2D1_SWEEP_DIRECTION_COUNTER_CLOCKWISE, large);
        native.EndFigure(D2D1_FIGURE_END.D2D1_FIGURE_END_CLOSED);
        native.Close().ThrowOnError();
        return geometry;

        D2D_POINT_2F Polar(float angle, float radius) => new(center.x + MathF.Cos(angle) * radius, center.y + MathF.Sin(angle) * radius);
    }

    private static D3DCOLORVALUE FromHsv(float hue, float saturation, float value)
    {
        hue = (hue % 1 + 1) % 1 * 6;
        var sector = (int)hue;
        var fraction = hue - sector;
        var p = value * (1 - saturation);
        var q = value * (1 - saturation * fraction);
        var t = value * (1 - saturation * (1 - fraction));
        // the fields are named, the constructor of D3DCOLORVALUE takes alpha first.
        return sector switch
        {
            0 => new D3DCOLORVALUE { r = value, g = t, b = p, a = 1 },
            1 => new D3DCOLORVALUE { r = q, g = value, b = p, a = 1 },
            2 => new D3DCOLORVALUE { r = p, g = value, b = t, a = 1 },
            3 => new D3DCOLORVALUE { r = p, g = q, b = value, a = 1 },
            4 => new D3DCOLORVALUE { r = t, g = p, b = value, a = 1 },
            _ => new D3DCOLORVALUE { r = value, g = p, b = q, a = 1 },
        };
    }
}
