namespace Treemapolis.Rendering;

// the island on the left of the window: the drives and the places of the Desktop as tiles on a floating slab.
// it has a viewport and a camera of its own, tilted and orthographic, so it stays put whatever the map camera does,
// and it is small enough to be hit tested on the CPU against its projected tiles rather than through the entry buffer.
public sealed class IslandRenderer : IDisposable
{
    private const int _maximumInstances = 256;
    private const uint _boxVertexCount = 36;
    private const float _tileWidth = 8;
    private const float _platformMargin = 0.7f;
    private const float _driveDepth = 3.4f;
    private const float _placeDepth = 1.7f;
    private const float _rowGap = 0.4f;
    private const float _groupGap = 1.0f;
    private const float _platformHeight = 1.0f;
    private const float _tileHeight = 0.6f;
    private const float _fillHeight = 0.12f;
    private const float _fillInset = 0.35f;
    private const float _fillFrontInset = 0.25f;
    private const float _fillDepthRatio = 0.12f;
    private const float _pitch = 0.6f;
    private const float _cameraDistance = 100;
    private const float _nearPlane = 1;
    private const float _farPlane = 400;
    private const float _widthFit = 1.04f;
    private const float _viewMarginPixels = 6;
    private const float _hoverLift = 0.3f;
    private const float _hoverRate = 14;
    private const float _pressDepth = 0.45f;
    private const float _pressRate = 11;
    private const float _pressDamping = 3.2f;
    private const float _pressOvershoot = 0.15f;
    private const float _clickSeconds = 1.2f;
    private const float _hoverGlow = 0.35f;
    private const float _settled = 0.001f;
    private const float _lowSpaceRatio = 0.9f;
    private const uint _platformColor = 0x26303B;
    private const uint _placeColor = 0x3A4B5E;
    private const uint _driveColor = 0x303D4B;
    private const uint _fillColor = 0x3E8FD0;
    private const uint _lowFillColor = 0xD05A5A;
    private static readonly Vector3 _lightDirection = Vector3.Normalize(new Vector3(-0.3f, -1, -0.5f));

    private readonly GraphicsDevice _device;
    private readonly IReadOnlyList<DXGI_FORMAT> _renderTargetFormats;
    private readonly DXGI_FORMAT _depthFormat;
    private readonly Shader _vertexShader;
    private readonly Shader _pixelShader;
    private readonly RootSignature _rootSignature;
    private readonly UploadBuffer _instances;
    private readonly ConstantBuffer<FrameConstants> _constants;
    private readonly IslandInstance[] _data = new IslandInstance[_maximumInstances];
    private readonly List<Tile> _tiles = [];
    private readonly List<IslandLabel> _labels = [];
    private PipelineState _pipeline;
    private IReadOnlyList<Place> _places = [];
    private int _instanceCount;
    private float _contentDepth;
    private float _scroll;
    private Matrix4x4 _viewProjection;
    private Vector3 _eye;
    private double _lastTime;

    public IslandRenderer(GraphicsDevice device, uint frameSlots, IReadOnlyList<DXGI_FORMAT> renderTargetFormats, DXGI_FORMAT depthFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(renderTargetFormats);
        _device = device;
        _renderTargetFormats = renderTargetFormats;
        _depthFormat = depthFormat;
        _vertexShader = new Shader("Island.vs");
        _pixelShader = new Shader("Island.ps");
        _rootSignature = new RootSignature(device, _vertexShader);
        _pipeline = CreatePipeline(1);
        _instances = new UploadBuffer(device, (ulong)(_maximumInstances * Unsafe.SizeOf<IslandInstance>()));
        _constants = new ConstantBuffer<FrameConstants>(device, frameSlots);
    }

    public bool IsVisible { get; set; } = true;

    // client pixels.
    public D2D_RECT_F Viewport { get; set; }

    // the file system path or parsing name the map shows, the tile holding it keeps an outline.
    public string? CurrentLocation { get; set; }

    public int HoveredTile { get; private set; } = -1;
    public int InstanceCount => _instanceCount;
    public bool IsAnimating { get; private set; }
    public IReadOnlyList<IslandLabel> Labels => _labels;
    public Place? HoveredPlace => HoveredTile >= 0 ? _places[_tiles[HoveredTile].Place] : null;

    public bool Contains(float x, float y) => IsVisible && _tiles.Count > 0 && x >= Viewport.left && x < Viewport.right && y >= Viewport.top && y < Viewport.bottom;

    public void SetSampleCount(uint sampleCount)
    {
        _pipeline.Dispose();
        _pipeline = CreatePipeline(sampleCount);
    }

    // drives first, a gap, then the places, each a row of the slab.
    public void SetPlaces(IReadOnlyList<Place> places)
    {
        ArgumentNullException.ThrowIfNull(places);
        _places = places;
        _tiles.Clear();
        HoveredTile = -1;
        var z = 0f;
        var instance = 1;
        for (var i = 0; i < places.Count && instance < _maximumInstances - 2; i++)
        {
            var place = places[i];
            if (i > 0 && places[i - 1].IsDrive && !place.IsDrive)
            {
                z += _groupGap;
            }

            var depth = place.IsDrive ? _driveDepth : _placeDepth;
            var tile = new Tile { Place = i, Instance = instance++, Z = z + depth * 0.5f, Depth = depth };
            _data[tile.Instance] = new IslandInstance
            {
                Position = new Vector3(0, _platformHeight, tile.Z),
                Size = new Vector3(_tileWidth, _tileHeight, depth),
                Color = place.IsDrive ? _driveColor : _placeColor,
            };

            // a drive carries a bar of what is used along the front of its top, as long as that share of the tile.
            if (place.IsDrive && place.Capacity > 0)
            {
                var used = Math.Clamp((place.Capacity - place.FreeSpace) / (float)place.Capacity, 0, 1);
                var width = (_tileWidth - 2 * _fillInset) * used;
                tile.FillInstance = instance++;
                _data[tile.FillInstance] = new IslandInstance
                {
                    Position = new Vector3(-_tileWidth * 0.5f + _fillInset + width * 0.5f, _platformHeight + _tileHeight, tile.Z + depth * (0.5f - _fillDepthRatio * 0.5f) - _fillFrontInset),
                    Size = new Vector3(MathF.Max(width, 0.01f), _fillHeight, depth * _fillDepthRatio),
                    Color = used >= _lowSpaceRatio ? _lowFillColor : _fillColor,
                };
            }

            _tiles.Add(tile);
            z += depth + _rowGap;
        }

        _contentDepth = MathF.Max(0, z - _rowGap);
        _data[0] = new IslandInstance
        {
            Position = new Vector3(0, 0, _contentDepth * 0.5f),
            Size = new Vector3(_tileWidth + 2 * _platformMargin, _platformHeight, _contentDepth + 2 * _platformMargin),
            Color = _platformColor,
        };
        _instanceCount = instance;
    }

    public int HitTest(float x, float y)
    {
        if (!Contains(x, y))
            return -1;

        Span<Vector2> quad = stackalloc Vector2[4];
        for (var i = 0; i < _tiles.Count; i++)
        {
            var tile = _tiles[i];
            var lift = _data[tile.Instance].Lift;
            var x0 = -_tileWidth * 0.5f;
            var x1 = _tileWidth * 0.5f;
            var z0 = tile.Z - tile.Depth * 0.5f;
            var z1 = tile.Z + tile.Depth * 0.5f;
            var bottom = _platformHeight + lift;
            var top = bottom + _tileHeight;
            quad[0] = Project(new Vector3(x0, top, z0));
            quad[1] = Project(new Vector3(x1, top, z0));
            quad[2] = Project(new Vector3(x1, top, z1));
            quad[3] = Project(new Vector3(x0, top, z1));
            if (IsInside(quad, x, y))
                return i;

            quad[0] = Project(new Vector3(x0, bottom, z1));
            quad[1] = Project(new Vector3(x1, bottom, z1));
            quad[2] = Project(new Vector3(x1, top, z1));
            quad[3] = Project(new Vector3(x0, top, z1));
            if (IsInside(quad, x, y))
                return i;
        }
        return -1;
    }

    // true when what is under the pointer changed.
    public bool SetHover(float x, float y)
    {
        var tile = HitTest(x, y);
        if (tile == HoveredTile)
            return false;

        HoveredTile = tile;
        return true;
    }

    public void ClearHover() => HoveredTile = -1;

    public void Scroll(float pixels) => _scroll += pixels;

    // the tile is pushed into the slab, springs back and lights up, the caller opens the place.
    public Place? Press(int tile, double time)
    {
        if (tile < 0 || tile >= _tiles.Count)
            return null;

        _tiles[tile].ClickTime = time;
        IsAnimating = true;
        return _places[_tiles[tile].Place];
    }

    public void Update(double time, float scale)
    {
        var deltaTime = (float)Math.Clamp(time - _lastTime, 0, 0.1);
        _lastTime = time;
        UpdateCamera(scale);

        var current = CurrentLocation;
        var selected = FindCurrentTile(current);
        var animating = false;
        _labels.Clear();
        for (var i = 0; i < _tiles.Count; i++)
        {
            var tile = _tiles[i];
            var target = i == HoveredTile ? 1f : 0f;
            tile.Hover += (target - tile.Hover) * MathF.Min(1, deltaTime * _hoverRate);
            if (MathF.Abs(target - tile.Hover) > _settled)
            {
                animating = true;
            }
            else
            {
                tile.Hover = target;
            }

            var age = (float)(time - tile.ClickTime);
            var jump = 0f;
            var flash = 0f;
            if (age >= 0 && age < _clickSeconds)
            {
                jump = -_pressDepth * MathF.Exp(-age * _pressDamping) * MathF.Sin(age * _pressRate);
                flash = 1 - age / _clickSeconds;
                animating = true;
            }

            var lift = tile.Hover * _hoverLift + jump;
            ref var data = ref _data[tile.Instance];
            data.Lift = lift;
            data.Glow = MathF.Max(tile.Hover * _hoverGlow, flash);
            data.Selected = i == selected ? 1 : 0;
            if (tile.FillInstance > 0)
            {
                ref var fill = ref _data[tile.FillInstance];
                fill.Lift = lift;
                fill.Glow = data.Glow;
            }

            var place = _places[tile.Place];
            var bounds = TopFaceBounds(tile, lift);
            var detail = place.IsDrive && place.Capacity > 0 ? string.Format(CultureInfo.CurrentCulture, Res.IslandDriveDetail, Navigator.FormatBytes(place.FreeSpace), Navigator.FormatBytes(place.Capacity)) : null;
            _labels.Add(new IslandLabel(place, detail, bounds));
        }

        IsAnimating = animating;
        _instances.Write<IslandInstance>(_data.AsSpan(0, _instanceCount));
    }

    // drawn after the map in its own part of the window, over a depth cleared there so the map never cuts into it.
    public unsafe void Render(CommandList list, uint frameSlot, in FrameConstants frame, DescriptorHandle depth)
    {
        ArgumentNullException.ThrowIfNull(list);
        var viewport = Viewport;
        if (!IsVisible || _instanceCount == 0 || viewport.right <= viewport.left || viewport.bottom <= viewport.top)
            return;

        var constants = frame;
        constants.ViewProjection = _viewProjection;
        constants.CameraPosition = _eye;
        constants.LightDirection = _lightDirection;
        _constants.Write(frameSlot, constants);

        var native = list.NativeObject;
        var rect = new RECT((int)viewport.left, (int)viewport.top, (int)viewport.right, (int)viewport.bottom);
        native.RSSetViewports(1, [new D3D12_VIEWPORT { TopLeftX = rect.left, TopLeftY = rect.top, Width = rect.Width, Height = rect.Height, MaxDepth = 1 }]);
        native.RSSetScissorRects(1, [rect]);
        native.ClearDepthStencilView(depth.Cpu, D3D12_CLEAR_FLAGS.D3D12_CLEAR_FLAG_DEPTH, 0, 0, 1, (nint)(&rect));
        native.SetGraphicsRootSignature(_rootSignature.NativeObject);
        native.SetPipelineState(_pipeline.NativeObject);
        native.SetGraphicsRootConstantBufferView(0, _constants.GetGpuVirtualAddress(frameSlot));
        native.SetGraphicsRootShaderResourceView(1, _instances.GpuVirtualAddress);
        native.DrawInstanced(_boxVertexCount, (uint)_instanceCount, 0, 0);
    }

    // tilted, orthographic, as wide as the slab, its top at the top of the viewport, the rest scrolled with the wheel.
    private void UpdateCamera(float scale)
    {
        var viewport = Viewport;
        var width = MathF.Max(1, viewport.right - viewport.left);
        var height = MathF.Max(1, viewport.bottom - viewport.top);
        var target = new Vector3(0, _platformHeight, _contentDepth * 0.5f);
        _eye = target + new Vector3(0, MathF.Sin(_pitch), MathF.Cos(_pitch)) * _cameraDistance;
        var view = Matrix4x4.CreateLookAt(_eye, target, Vector3.UnitY);

        var pixelsPerUnit = width / ((_tileWidth + 2 * _platformMargin) * _widthFit);
        var visibleWidth = width / pixelsPerUnit;
        var visibleHeight = height / pixelsPerUnit;
        // a click pushes a tile down and it springs back only a little higher than a hovered one, so the slab starts right under the caption.
        var platformTop = Vector3.Transform(new Vector3(0, _platformHeight, -_platformMargin), view).Y;
        var tileTop = Vector3.Transform(new Vector3(0, _platformHeight + _tileHeight + _hoverLift + _pressOvershoot, 0), view).Y;
        var top = MathF.Max(platformTop, tileTop);
        var bottom = Vector3.Transform(new Vector3(0, 0, _contentDepth + _platformMargin), view).Y;
        var margin = _viewMarginPixels * scale / pixelsPerUnit;
        var maximumScroll = MathF.Max(0, (top - bottom + 2 * margin - visibleHeight) * pixelsPerUnit);
        _scroll = Math.Clamp(_scroll, 0, maximumScroll);

        var viewTop = top + margin - _scroll / pixelsPerUnit;
        _viewProjection = view * Camera.CreateReversedOrthographic(-visibleWidth * 0.5f, visibleWidth * 0.5f, viewTop - visibleHeight, viewTop, _nearPlane, _farPlane);
    }

    // the deepest place holding what the map shows, a user folder rather than the drive it is on.
    private int FindCurrentTile(string? current)
    {
        if (string.IsNullOrEmpty(current))
            return -1;

        var best = -1;
        var bestLength = 0;
        for (var i = 0; i < _tiles.Count; i++)
        {
            var place = _places[_tiles[i].Place];
            var match = place.ParsingName.Equals(current, StringComparison.OrdinalIgnoreCase) ? place.ParsingName.Length : 0;
            var path = place.FileSystemPath;
            if (match == 0 && path != null && current.StartsWith(path, StringComparison.OrdinalIgnoreCase) &&
                (current.Length == path.Length || path.EndsWith(Path.DirectorySeparatorChar) || current[path.Length] == Path.DirectorySeparatorChar))
            {
                match = path.Length;
            }

            if (match > bestLength)
            {
                best = i;
                bestLength = match;
            }
        }
        return best;
    }

    private D2D_RECT_F TopFaceBounds(Tile tile, float lift)
    {
        var top = _platformHeight + lift + _tileHeight;
        var a = Project(new Vector3(-_tileWidth * 0.5f, top, tile.Z - tile.Depth * 0.5f));
        var b = Project(new Vector3(_tileWidth * 0.5f, top, tile.Z + tile.Depth * 0.5f));
        return new D2D_RECT_F { left = MathF.Min(a.X, b.X), top = MathF.Min(a.Y, b.Y), right = MathF.Max(a.X, b.X), bottom = MathF.Max(a.Y, b.Y) };
    }

    private Vector2 Project(Vector3 point)
    {
        var clip = Vector4.Transform(new Vector4(point, 1), _viewProjection);
        var viewport = Viewport;
        return new Vector2(
            viewport.left + (clip.X / clip.W * 0.5f + 0.5f) * (viewport.right - viewport.left),
            viewport.top + (0.5f - clip.Y / clip.W * 0.5f) * (viewport.bottom - viewport.top));
    }

    private static bool IsInside(ReadOnlySpan<Vector2> quad, float x, float y)
    {
        var sign = 0;
        for (var i = 0; i < quad.Length; i++)
        {
            var a = quad[i];
            var b = quad[(i + 1) % quad.Length];
            var cross = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
            var side = cross > 0 ? 1 : cross < 0 ? -1 : 0;
            if (side != 0)
            {
                if (sign != 0 && side != sign)
                    return false;

                sign = side;
            }
        }
        return true;
    }

    private PipelineState CreatePipeline(uint sampleCount) => PipelineState.CreateGraphics(_device, new GraphicsPipelineDescription
    {
        RootSignature = _rootSignature,
        VertexShader = _vertexShader,
        PixelShader = _pixelShader,
        RenderTargetFormats = _renderTargetFormats,
        DepthStencilFormat = _depthFormat,
        SampleCount = sampleCount,
    });

    public void Dispose()
    {
        _pipeline.Dispose();
        _rootSignature.Dispose();
        _instances.Dispose();
        _constants.Dispose();
    }

    private sealed class Tile
    {
        public int Place { get; init; }
        public int Instance { get; init; }
        public int FillInstance { get; set; }
        public float Z { get; init; }
        public float Depth { get; init; }
        public float Hover { get; set; }
        public double ClickTime { get; set; } = double.MinValue;
    }
}
