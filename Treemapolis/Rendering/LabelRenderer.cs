namespace Treemapolis.Rendering;

public sealed class LabelRenderer : IDisposable
{
    private const double _selectionIntervalSeconds = 0.1;
    private const int _maxRasterizedPerPass = 96;
    private const int _maxLabels = 1024;
    private const uint _labelVertexCount = 6;

    private readonly LabelAtlas _atlas;
    private readonly RootSignature _rootSignature;
    private readonly GraphicsDevice _device;
    private readonly IReadOnlyList<DXGI_FORMAT> _renderTargetFormats;
    private readonly DXGI_FORMAT _depthFormat;
    private readonly Shader _vertexShader;
    private readonly Shader _pixelShader;
    private PipelineState _pipeline;
    private readonly UploadBuffer[] _instanceBuffers;
    private readonly LabelInstance[] _labels = new LabelInstance[_maxLabels];
    private Task<LabelSelection>? _selecting;
    private LabelSelection? _selection;
    private LabelSelection? _latestSelection;
    private bool _showThumbnails;
    private float _minimumThumbnailPixels = Settings.DefaultMinimumThumbnailPixels;
    private LayoutSnapshot? _selectedLayout;
    private NamespaceTree? _labelsTree;
    private Matrix4x4 _selectedViewProjection;
    private double _lastSelectionSeconds = double.MinValue;
    private int _labelCount;
    private bool _pending;

    public LabelRenderer(GraphicsDevice device, DescriptorHeaps heaps, uint frameSlots, IReadOnlyList<DXGI_FORMAT> renderTargetFormats, DXGI_FORMAT depthFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(heaps);
        _atlas = new LabelAtlas(device, heaps, frameSlots);

        _device = device;
        _renderTargetFormats = renderTargetFormats;
        _depthFormat = depthFormat;
        _vertexShader = new Shader("Labels.vs");
        _pixelShader = new Shader("Labels.ps");
        _rootSignature = new RootSignature(device, _vertexShader);
        _pipeline = CreatePipeline(1);

        _instanceBuffers = new UploadBuffer[frameSlots];
        for (var i = 0; i < frameSlots; i++)
        {
            _instanceBuffers[i] = new UploadBuffer(device, (ulong)(_maxLabels * Unsafe.SizeOf<LabelInstance>()));
        }
    }

    // a pipeline is made for one sample count, a new count needs a new one.
    public void SetSampleCount(uint sampleCount)
    {
        _pipeline.Dispose();
        _pipeline = CreatePipeline(sampleCount);
    }

    private PipelineState CreatePipeline(uint sampleCount) => PipelineState.CreateGraphics(_device, new GraphicsPipelineDescription
    {
        RootSignature = _rootSignature,
        VertexShader = _vertexShader,
        PixelShader = _pixelShader,
        RenderTargetFormats = _renderTargetFormats,
        DepthStencilFormat = _depthFormat,
        CullMode = D3D12_CULL_MODE.D3D12_CULL_MODE_NONE,
        DepthWrite = false,
        AlphaBlend = true,
        SampleCount = sampleCount,
    });

    public int LabelCount => _labelCount;
    public bool IsVisible { get; set; } = true;

    // whether the selection also picks the files close enough for a thumbnail.
    public bool ShowThumbnails
    {
        get => _showThumbnails;
        set
        {
            _pending |= value != _showThumbnails;
            _showThumbnails = value;
        }
    }

    // how large the top of a file block must be on screen, in pixels, before its thumbnail is asked for.
    public float MinimumThumbnailPixels
    {
        get => _minimumThumbnailPixels;
        set
        {
            _pending |= value != _minimumThumbnailPixels;
            _minimumThumbnailPixels = value;
        }
    }
    // the last selection made, which stays after its labels are all built.
    public LabelSelection? Selection => _latestSelection;
    public int AtlasEntries => _atlas.Count;
    public bool IsBusy => _pending || (_selecting != null && !_selecting.IsCompleted);

    public void Update(LayoutSnapshot? layout, in Matrix4x4 viewProjection, Vector3 cameraPosition, float pixelScale, double time)
    {
        if (_selecting != null && _selecting.IsCompleted)
        {
            if (_selecting.IsCompletedSuccessfully)
            {
                _selection = _selecting.Result;
                _latestSelection = _selection;
            }
            _selecting = null;
        }

        // labels name entries of the tree they were chosen in. once another tree is shown they are dropped,
        // its instance buffer can be shorter than their entries, and the vertex shader would read past its end.
        if (_selection != null && _selection.Layout.Tree != layout?.Tree)
        {
            _selection = null;
        }

        if (_labelsTree != layout?.Tree)
        {
            _labelCount = 0;
            _labelsTree = null;
        }

        if (_selection != null)
        {
            BuildLabels(_selection);
        }

        if (layout == null)
        {
            _labelCount = 0;
            return;
        }

        if (layout != _selectedLayout || viewProjection != _selectedViewProjection)
        {
            _pending = true;
        }

        if (!_pending || _selecting != null || time - _lastSelectionSeconds < _selectionIntervalSeconds)
            return;

        _pending = false;
        _lastSelectionSeconds = time;
        _selectedLayout = layout;
        _selectedViewProjection = viewProjection;
        var matrix = viewProjection;
        var generation = _atlas.Generation;
        var showThumbnails = ShowThumbnails;
        var minimumThumbnailPixels = MinimumThumbnailPixels;
        _selecting = Task.Run(() => LabelSelection.Select(layout, matrix, cameraPosition, pixelScale, generation, showThumbnails, minimumThumbnailPixels));
    }

    private void BuildLabels(LabelSelection selection)
    {
        var tree = selection.Layout.Tree;
        _labelsTree = tree;
        var atlasSize = (float)LabelAtlas.Size;
        var rasterized = 0;
        var count = 0;
        var complete = true;
        foreach (var candidate in selection.Candidates)
        {
            if (count == _maxLabels)
                break;

            var style = candidate.Kind == LabelInstance.ContainerKind ? TextStyle.Container : TextStyle.File;
            var key = new LabelKey(style, tree.GetName(candidate.Entry).ToString());
            if (!_atlas.TryGet(key, out var entry))
            {
                // the rest are drawn once a later frame has rasterized them, a burst of new names never stalls one frame.
                if (rasterized == _maxRasterizedPerPass)
                {
                    complete = false;
                    continue;
                }

                if (!_atlas.TryAdd(key, key.Text, out entry))
                {
                    _atlas.Reset();
                    _pending = true;
                    count = 0;
                    break;
                }
                rasterized++;
            }

            if (entry.Width == 0)
                continue;

            // a long name shrinks to its width limit rather than spilling over its neighbours.
            var worldWidth = MathF.Min(candidate.WorldHeight * entry.Width / entry.Height, candidate.MaxWorldWidth);
            var worldHeight = worldWidth * entry.Height / entry.Width;
            if (candidate.Pixels * worldHeight / candidate.WorldHeight < LabelSelection.MinimumPixels)
                continue;

            _labels[count++] = new LabelInstance
            {
                Entry = candidate.Entry,
                Kind = candidate.Kind,
                UvOffset = new Vector2(entry.X / atlasSize, entry.Y / atlasSize),
                UvSize = new Vector2(entry.Width / atlasSize, entry.Height / atlasSize),
                WorldSize = new Vector2(worldWidth, worldHeight),

                // a name shrunk to fit stays centered in the band it was placed in.
                FrontInset = candidate.FrontInset + (candidate.WorldHeight - worldHeight) * 0.5f,
            };
        }

        _labelCount = count;
        if (complete)
        {
            _selection = null;
        }
    }

    public void Render(CommandList list, uint frameSlot, ulong frameAddress, GpuBuffer current)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(current);
        _atlas.Texture.Upload(list, frameSlot);
        if (_labelCount == 0 || !IsVisible)
            return;

        var buffer = _instanceBuffers[frameSlot];
        buffer.Write<LabelInstance>(_labels.AsSpan(0, _labelCount));
        list.Transition(_atlas.Texture.Texture, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);

        var native = list.NativeObject;
        native.SetGraphicsRootSignature(_rootSignature.NativeObject);
        native.SetPipelineState(_pipeline.NativeObject);
        native.SetGraphicsRootConstantBufferView(0, frameAddress);
        native.SetGraphicsRootShaderResourceView(1, buffer.GpuVirtualAddress);
        native.SetGraphicsRootShaderResourceView(2, current.GpuVirtualAddress);
        native.SetGraphicsRootDescriptorTable(3, _atlas.Texture.Texture.ShaderResourceView.Gpu);
        native.DrawInstanced(_labelVertexCount, (uint)_labelCount, 0, 0);
    }

    public void Dispose()
    {
        foreach (var buffer in _instanceBuffers)
        {
            buffer.Dispose();
        }

        _pipeline.Dispose();
        _rootSignature.Dispose();
        _atlas.Dispose();
    }
}
