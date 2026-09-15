namespace Treemapolis.Rendering;

// the whole map is GPU driven. the CPU uploads where entries should be, and only when the layout changes.
// every frame a compute pass eases every instance towards its target, a second one culls in chunks,
// two more compact what survived into one list, and a single ExecuteIndirect draws all of it, folders and files alike.
public sealed class SceneRenderer : IDisposable
{
    private const DXGI_FORMAT _depthFormat = DXGI_FORMAT.DXGI_FORMAT_D32_FLOAT;
    private const DXGI_FORMAT _entryFormat = DXGI_FORMAT.DXGI_FORMAT_R32_UINT;
    private const DXGI_FORMAT _colorFormat = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_TYPELESS;
    private const uint _resolveThreadGroupSize = 16;
    private const uint _noEntry = uint.MaxValue;
    private const uint _chunkSize = 1024;
    private const uint _chunkThreadGroupSize = 64;
    private const uint _animateThreadGroupSize = 1024;
    private const uint _currentStride = 32;
    private const uint _minimumCapacity = 64 * _chunkSize;
    private const double _growthFactor = 1.25;
    private const double _tweenSeconds = 1.5;
    private const float _tweenRate = 7;
    private const float _minimumPixels = 0.5f;
    private const float _groundMargin = 2;
    private const float _minimumGroundExtent = 400;
    private const float _fogReach = 1.5f;
    private const float _minimumFogExtent = 150;
    private const uint _groundVertexCount = 6;
    private const uint _postVertexCount = 3;
    private const uint _edgeArgumentsOffset = 16;
    private const float _newYorkEdgeWidth = 2.5f;
    private const float _neonEdgeWidth = 8;
    private const uint _lineNeon = 1;
    private const uint _lineNewYork = 2;
    private const uint _shadowMapSize = 4096;
    private const float _shadowReach = 2.5f;
    private const float _minimumShadowRadius = 40;
    private const float _shadowOctaveSteps = 4;
    private const float _shadowDepthMargin = 10;
    private const float _shadowMinimumTexels = 0.5f;
    private const int _shadowDepthBias = -2;
    private const float _shadowSlopeBias = -1.5f;
    private const uint _boxVertexCount = 36;
    private const float _sunHeight = 0.8f;
    private const float _sunSpread = 0.7f;

    private readonly GraphicsDevice _device;
    private readonly DescriptorHeaps _heaps;
    private readonly DXGI_FORMAT[] _sceneFormats;
    private readonly Shader _blocksVertex;
    private readonly Shader _blocksPixel;
    private readonly Shader _groundVertex;
    private readonly Shader _groundPixel;
    private readonly RootSignature _blocksRootSignature;
    private readonly RootSignature _groundRootSignature;
    private readonly RootSignature _resolveRootSignature;
    private readonly Shader _edgesVertex;
    private readonly Shader _edgesPixel;
    private readonly RootSignature _edgesRootSignature;
    private PipelineState? _edgesPipeline;
    private readonly RootSignature _postRootSignature;
    private readonly PipelineState _postPipeline;
    private readonly D3D12_CPU_DESCRIPTOR_HANDLE[] _postTargets = new D3D12_CPU_DESCRIPTOR_HANDLE[1];
    private readonly int _effectScope;
    private ScreenEffect _effect;
    private readonly Shader _shadowVertex;
    private readonly RootSignature _shadowRootSignature;
    private readonly PipelineState _shadowPipeline;
    private readonly Texture _shadowMap;
    private readonly ConstantBuffer<FrameConstants> _shadowConstants;
    private readonly int _shadowScope;
    private readonly PipelineState _resolvePipeline;
    private PipelineState _blocksPipeline;
    private PipelineState _groundPipeline;
    private Texture? _color;
    private Texture? _resolved;
    private Texture? _pickEntries;
    private Vector4 _skyColor = Vector4.UnitW;
    private readonly RootSignature _animateRootSignature;
    private readonly PipelineState _animatePipeline;
    private readonly RootSignature _cullRootSignature;
    private readonly PipelineState _cullPipeline;
    private readonly RootSignature _offsetsRootSignature;
    private readonly PipelineState _offsetsPipeline;
    private readonly RootSignature _compactRootSignature;
    private readonly PipelineState _compactPipeline;
    private readonly CommandSignature _drawSignature;
    private readonly ConstantBuffer<FrameConstants> _frameConstants;
    private readonly ReadbackBuffer[] _argumentReadbacks;
    private readonly bool[] _argumentReadbackPending;
    private readonly uint[] _argumentReadbackInstances;
    private readonly D3D12_CPU_DESCRIPTOR_HANDLE[] _sceneTargets = new D3D12_CPU_DESCRIPTOR_HANDLE[2];
    private readonly List<IDisposable> _retired = [];
    private readonly int _uploadScope;
    private readonly int _animateScope;
    private readonly int _cullScope;
    private readonly int _drawScope;
    private readonly int _labelsScope;
    private InstanceBuffers? _buffers;
    private UploadBuffer? _upload;
    private LayoutSnapshot? _pendingLayout;
    private Texture? _depth;
    private Texture? _entries;
    private double _animateUntil;
    private double _lastTime;
    private bool _settled = true;

    public SceneRenderer(GraphicsDevice device, DescriptorHeaps heaps, uint frameSlots, DXGI_FORMAT backBufferFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(heaps);
        _device = device;
        _heaps = heaps;

        DXGI_FORMAT[] sceneFormats = [backBufferFormat, _entryFormat];
        _sceneFormats = sceneFormats;
        _blocksVertex = new Shader("Blocks.vs");
        _blocksPixel = new Shader("Blocks.ps");
        _groundVertex = new Shader("Ground.vs");
        _groundPixel = new Shader("Ground.ps");
        _blocksRootSignature = new RootSignature(device, _blocksVertex);
        _groundRootSignature = new RootSignature(device, _groundVertex);
        (_blocksPipeline, _groundPipeline) = CreateScenePipelines(1);
        _edgesVertex = new Shader("Edges.vs");
        _edgesPixel = new Shader("Edges.ps");
        _edgesRootSignature = new RootSignature(device, _edgesVertex);
        (_resolveRootSignature, _resolvePipeline) = CreateCompute(device, "EntryResolve.cs");
        SampleCounts = FeatureSupport.GetSampleCounts(device, [DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB, _entryFormat, _depthFormat]);

        (_animateRootSignature, _animatePipeline) = CreateCompute(device, "Animate.cs");
        (_cullRootSignature, _cullPipeline) = CreateCompute(device, "Cull.cs");
        (_offsetsRootSignature, _offsetsPipeline) = CreateCompute(device, "Offsets.cs");
        (_compactRootSignature, _compactPipeline) = CreateCompute(device, "Compact.cs");
        _drawSignature = CommandSignature.CreateDraw(device);

        // the casters draw their back faces into the map, a lit face is then compared with the far side of its own block, never with itself.
        _shadowVertex = new Shader("Shadow.vs");
        _shadowRootSignature = new RootSignature(device, _shadowVertex);
        _shadowPipeline = PipelineState.CreateGraphics(device, new GraphicsPipelineDescription
        {
            RootSignature = _shadowRootSignature,
            VertexShader = _shadowVertex,
            DepthStencilFormat = _depthFormat,
            CullMode = D3D12_CULL_MODE.D3D12_CULL_MODE_FRONT,
            DepthBias = _shadowDepthBias,
            SlopeScaledDepthBias = _shadowSlopeBias,
        });
        _shadowMap = new Texture(device, heaps, new TextureDescription
        {
            Width = _shadowMapSize,
            Height = _shadowMapSize,
            Format = _depthFormat,
            DepthStencil = true,
            ShaderResource = true,
            ClearDepth = 0,
            InitialState = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_DEPTH_WRITE,
        });

        var postVertex = new Shader("Post.vs");
        _postRootSignature = new RootSignature(device, postVertex);
        _postPipeline = PipelineState.CreateGraphics(device, new GraphicsPipelineDescription
        {
            RootSignature = _postRootSignature,
            VertexShader = postVertex,
            PixelShader = new Shader("Post.ps"),
            RenderTargetFormats = [backBufferFormat],
            DepthTest = false,
            DepthWrite = false,
            CullMode = D3D12_CULL_MODE.D3D12_CULL_MODE_NONE,
        });

        _frameConstants = new ConstantBuffer<FrameConstants>(device, frameSlots);
        _shadowConstants = new ConstantBuffer<FrameConstants>(device, frameSlots);
        _argumentReadbacks = new ReadbackBuffer[frameSlots];
        _argumentReadbackPending = new bool[frameSlots];
        _argumentReadbackInstances = new uint[frameSlots];
        for (var i = 0; i < frameSlots; i++)
        {
            _argumentReadbacks[i] = new ReadbackBuffer(device, (ulong)Unsafe.SizeOf<D3D12_DRAW_ARGUMENTS>());
        }

        Timer = new GpuTimer(device, frameSlots);
        _uploadScope = Timer.Register(Res.PassUpload);
        _animateScope = Timer.Register(Res.PassAnimate);
        _cullScope = Timer.Register(Res.PassCull);
        _shadowScope = Timer.Register(Res.PassShadows);
        _drawScope = Timer.Register(Res.PassDraw);
        _labelsScope = Timer.Register(Res.PassLabels);
        _effectScope = Timer.Register(Res.PassEffect);
        Labels = new LabelRenderer(device, heaps, frameSlots, sceneFormats, _depthFormat);
        Picks = new PickBuffer(device, frameSlots);
        Thumbnails = new ThumbnailAtlas(device, heaps, frameSlots);
        Island = new IslandRenderer(device, frameSlots, sceneFormats, _depthFormat);
    }

    public GpuTimer Timer { get; }
    public RenderJournal Journal { get; } = new();
    public IReadOnlyList<uint> SampleCounts { get; }
    public uint SampleCount { get; private set; } = 1;
    public LabelRenderer Labels { get; }

    // premultiplied, the clear color and what distance fades into.
    // a multisampled color target is created with it as its clear value, so a new sky makes new targets.
    public Vector4 SkyColor
    {
        get => _skyColor;
        set
        {
            if (value == _skyColor)
                return;

            _skyColor = value;
            if (_color != null)
            {
                _device.DirectQueue.WaitForIdle();
                DisposeTargets();
            }
        }
    }
    public float FogScale { get; set; } = 1;
    public PickBuffer Picks { get; }
    public ThumbnailAtlas Thumbnails { get; }
    public IslandRenderer Island { get; }
    public bool ShowThumbnails { get; set; } = true;
    public bool ShowShadows { get; set; } = true;

    // a post effect renders the scene offscreen and the line effects clear to black,
    // switching between kinds changes the targets or their clear color, so they are made again.
    public ScreenEffect Effect
    {
        get => _effect;
        set
        {
            if (value == _effect)
                return;

            var targetsChange = IsPostEffect(value) != IsPostEffect(_effect) || LineEffectOf(value) != LineEffectOf(_effect);
            _effect = value;
            if (targetsChange && _depth != null)
            {
                _device.DirectQueue.WaitForIdle();
                DisposeTargets();
            }
        }
    }

    // the monitor scale, lines and big pixels are measured with it.
    public float EffectScale { get; set; } = 1;

    // where the sun's light goes on the ground, in degrees, zero sends it and the shadows towards the default camera.
    public float SunAngle { get; set; }

    private Vector3 LightDirection
    {
        get
        {
            var angle = SunAngle * MathF.PI / 180;
            return Vector3.Normalize(new Vector3(MathF.Sin(angle) * _sunSpread, -_sunHeight, MathF.Cos(angle) * _sunSpread));
        }
    }
    public Ground Ground { get; set; }
    public int HoveredEntry { get; set; } = Entry.None;
    public int SelectedEntry { get; set; } = Entry.None;
    public LayoutSnapshot? Layout { get; private set; }
    public uint InstanceCount { get; private set; }
    public uint Capacity => _buffers?.Capacity ?? 0;
    public uint VisibleInstances { get; private set; }
    public bool IsTransitioning => !_settled || _pendingLayout != null || Labels.IsBusy;

    // the selection pulses, so a scene with one keeps drawing.
    public bool IsAnimating => IsTransitioning || Picks.IsPending || SelectedEntry != Entry.None;

    public void SetLayout(LayoutSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _pendingLayout = snapshot;
    }

    // what the frame slot about to be reused picked, read before the slot records new requests.
    public void CollectPicks(SwapChain swapChain, List<PickResult> results)
    {
        ArgumentNullException.ThrowIfNull(swapChain);
        Picks.Collect(swapChain.FrameIndex, results);
    }

    public void Render(CommandList list, SwapChain swapChain, Camera camera, double time)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(swapChain);
        ArgumentNullException.ThrowIfNull(camera);

        var deltaTime = (float)Math.Clamp(time - _lastTime, 0, 0.25);
        _lastTime = time;
        EnsureTargets(swapChain.Width, swapChain.Height);
        var slot = swapChain.FrameIndex;
        Timer.BeginFrame(slot);
        ReadVisibleCount(slot);

        var native = list.NativeObject;
        list.SetDescriptorHeap(_heaps.ShaderResources);

        Timer.Begin(list, _uploadScope);
        var layoutUploaded = UploadPendingLayout(list, time);
        var thumbnailsUploaded = Thumbnails.Record(list, slot);
        Timer.End(list, _uploadScope);

        var view = camera.View;
        var viewProjection = view * camera.GetProjection(swapChain.Width / (float)swapChain.Height);
        var extent = Layout == null ? Vector3.Zero : Layout.BoundsMax - Layout.BoundsMin;
        var constants = new FrameConstants
        {
            ViewProjection = viewProjection,
            CameraPosition = camera.Position,
            Time = (float)time,
            LightDirection = LightDirection,
            InstanceCount = InstanceCount,
            Frustum = FrustumPlanes.FromViewProjection(viewProjection),
            PixelScale = camera.GetPixelScale(swapChain.Height),
            MinimumPixels = _minimumPixels,
            FogDensity = FogScale * _fogReach / MathF.Max(MathF.Max(extent.X, extent.Z) * 2, _minimumFogExtent),
            CameraRight = new Vector3(view.M11, view.M21, view.M31),
            CameraUp = new Vector3(view.M12, view.M22, view.M32),
            HoveredEntry = HoveredEntry < 0 ? _noEntry : (uint)HoveredEntry,
            SelectedEntry = SelectedEntry < 0 ? _noEntry : (uint)SelectedEntry,
            SkyColor = SceneSky,
            ShowThumbnails = ShowThumbnails ? 1u : 0u,
            LineEffect = LineEffectOf(_effect),
        };

        var shadows = ShowShadows && LineEffectOf(_effect) == 0 && Layout != null && InstanceCount > 0;
        var shadowAddress = 0ul;
        if (shadows)
        {
            var shadow = ComputeShadowView(camera, Layout!);
            constants.LightViewProjection = shadow.ViewProjection;
            constants.ShadowTexel = shadow.Texel;
            constants.ShowShadows = 1;

            // the sun's own constants cull and draw what it sees, tiny blocks less than half a texel wide cast nothing worth drawing.
            var sun = constants;
            sun.ViewProjection = shadow.ViewProjection;
            sun.Frustum = FrustumPlanes.FromViewProjection(shadow.ViewProjection);
            sun.CameraPosition = shadow.Eye;
            sun.PixelScale = shadow.PixelScale;
            sun.MinimumPixels = _shadowMinimumTexels;
            _shadowConstants.Write(slot, sun);
            shadowAddress = _shadowConstants.GetGpuVirtualAddress(slot);
        }
        _frameConstants.Write(slot, constants);
        Labels.Update(Layout, viewProjection, constants.CameraPosition, constants.PixelScale, time);
        var frameAddress = _frameConstants.GetGpuVirtualAddress(slot);

        // every scope is stamped every frame, resolving a query that was never written is an error.
        var buffers = _buffers;
        var hasInstances = InstanceCount > 0;
        var chunkCount = (InstanceCount + _chunkSize - 1) / _chunkSize;
        var animated = buffers != null && hasInstances && !_settled;
        Timer.Begin(list, _animateScope);
        if (buffers != null && hasInstances && !_settled)
        {
            var finalStep = time >= _animateUntil;
            var blend = finalStep ? 1 : 1 - MathF.Exp(-deltaTime * _tweenRate);
            list.Transition(buffers.Targets, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);
            list.Transition(buffers.Current, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            native.SetComputeRootSignature(_animateRootSignature.NativeObject);
            native.SetPipelineState(_animatePipeline.NativeObject);
            SetComputeConstants(native, 0, InstanceCount, BitConverter.SingleToUInt32Bits(blend));
            native.SetComputeRootShaderResourceView(1, buffers.Targets.GpuVirtualAddress);
            native.SetComputeRootUnorderedAccessView(2, buffers.Current.GpuVirtualAddress);
            native.Dispatch((InstanceCount + _animateThreadGroupSize - 1) / _animateThreadGroupSize, 1, 1);
            _settled = finalStep;
        }
        Timer.End(list, _animateScope);

        Timer.Begin(list, _cullScope);
        if (buffers != null && hasInstances)
        {
            Cull(list, buffers, buffers.Camera, frameAddress, chunkCount);
            list.Transition(buffers.Camera.Arguments, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE);
            native.CopyBufferRegion(_argumentReadbacks[slot].NativeObject, 0, buffers.Camera.Arguments.NativeObject, 0, (ulong)Unsafe.SizeOf<D3D12_DRAW_ARGUMENTS>());
            _argumentReadbackPending[slot] = true;
            _argumentReadbackInstances[slot] = InstanceCount;
        }
        Timer.End(list, _cullScope);

        // the sun culls the blocks for itself, a tower out of the camera's sight still throws its shadow into it.
        Timer.Begin(list, _shadowScope);
        if (buffers != null && shadows)
        {
            Cull(list, buffers, buffers.Shadow, shadowAddress, chunkCount);
            list.Transition(buffers.Shadow.Visible, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);
            list.Transition(buffers.Shadow.Arguments, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT);
            list.Transition(_shadowMap, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_DEPTH_WRITE);
            list.SetRenderTargets([], _shadowMap.DepthStencilView);
            list.SetViewport(_shadowMapSize, _shadowMapSize);
            list.ClearDepth(_shadowMap.DepthStencilView, 0);
            native.IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            native.SetGraphicsRootSignature(_shadowRootSignature.NativeObject);
            native.SetPipelineState(_shadowPipeline.NativeObject);
            native.SetGraphicsRootConstantBufferView(0, shadowAddress);
            native.SetGraphicsRootShaderResourceView(1, buffers.Shadow.Visible.GpuVirtualAddress);
            native.SetGraphicsRootShaderResourceView(2, buffers.Current.GpuVirtualAddress);
            native.ExecuteIndirect(_drawSignature.NativeObject, 1, buffers.Shadow.Arguments.NativeObject, 0, null, 0);
        }
        list.Transition(_shadowMap, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);
        Timer.End(list, _shadowScope);

        // multisampled, the scene renders into its own color target and is resolved into the back buffer at the end.
        var backBuffer = swapChain.CurrentBuffer;
        Resource colorTarget = _color != null ? _color : backBuffer;
        var colorView = _color != null ? _color.RenderTargetView : backBuffer.RenderTargetView;
        list.Transition(colorTarget, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET);
        list.Transition(_depth!, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_DEPTH_WRITE);
        list.Transition(_entries!, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET);
        _sceneTargets[0] = colorView.Cpu;
        _sceneTargets[1] = _entries!.RenderTargetView.Cpu;
        list.SetRenderTargets(_sceneTargets, _depth!.DepthStencilView);
        list.SetViewport(swapChain.Width, swapChain.Height);
        list.ClearRenderTarget(colorView, SceneSky);
        list.ClearRenderTarget(_entries.RenderTargetView, Vector4.Zero);
        list.ClearDepth(_depth.DepthStencilView, 0);
        native.IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

        Timer.Begin(list, _drawScope);
        if (Layout != null && Ground != Ground.None)
        {
            var groundExtent = MathF.Max(MathF.Max(extent.X, extent.Z) * _groundMargin, _minimumGroundExtent);
            var center = (Layout.BoundsMin + Layout.BoundsMax) * 0.5f;
            native.SetGraphicsRootSignature(_groundRootSignature.NativeObject);
            native.SetPipelineState(_groundPipeline.NativeObject);
            native.SetGraphicsRootConstantBufferView(0, frameAddress);
            SetGroundConstants(native, new Vector4(center.X - groundExtent, center.Z - groundExtent, center.X + groundExtent, center.Z + groundExtent), Ground == Ground.Grid);
            native.SetGraphicsRootDescriptorTable(2, _shadowMap.ShaderResourceView.Gpu);
            native.DrawInstanced(_groundVertexCount, 1, 0, 0);
        }

        if (buffers != null && hasInstances)
        {
            list.Transition(buffers.Camera.Visible, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);
            list.Transition(buffers.Camera.Arguments, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT);
            native.SetGraphicsRootSignature(_blocksRootSignature.NativeObject);
            native.SetPipelineState(_blocksPipeline.NativeObject);
            native.SetGraphicsRootConstantBufferView(0, frameAddress);
            native.SetGraphicsRootShaderResourceView(1, buffers.Camera.Visible.GpuVirtualAddress);
            native.SetGraphicsRootShaderResourceView(2, buffers.Current.GpuVirtualAddress);
            native.SetGraphicsRootShaderResourceView(3, Thumbnails.EntryCells.GpuVirtualAddress);
            native.SetGraphicsRootShaderResourceView(4, Thumbnails.CellExtents.GpuVirtualAddress);
            native.SetGraphicsRootDescriptorTable(5, Thumbnails.Texture.ShaderResourceView.Gpu);
            native.SetGraphicsRootShaderResourceView(6, buffers.Targets.GpuVirtualAddress);
            native.SetGraphicsRootDescriptorTable(7, _shadowMap.ShaderResourceView.Gpu);
            native.ExecuteIndirect(_drawSignature.NativeObject, 1, buffers.Camera.Arguments.NativeObject, 0, null, 0);

            var lineEffect = LineEffectOf(_effect);
            if (lineEffect != 0)
            {
                native.SetGraphicsRootSignature(_edgesRootSignature.NativeObject);
                _edgesPipeline ??= CreateEdgesPipeline(SampleCount);
                native.SetPipelineState(_edgesPipeline.NativeObject);
                native.SetGraphicsRootConstantBufferView(0, frameAddress);
                native.SetGraphicsRootShaderResourceView(1, buffers.Camera.Visible.GpuVirtualAddress);
                native.SetGraphicsRootShaderResourceView(2, buffers.Current.GpuVirtualAddress);
                var scale = MathF.Max(1, EffectScale);
                SetEdgeConstants(native, new Vector4(swapChain.Width, swapChain.Height, (lineEffect == _lineNeon ? _neonEdgeWidth : _newYorkEdgeWidth) * scale, scale));
                native.ExecuteIndirect(_drawSignature.NativeObject, 1, buffers.Camera.Arguments.NativeObject, _edgeArgumentsOffset, null, 0);
            }
        }
        Timer.End(list, _drawScope);

        Timer.Begin(list, _labelsScope);
        if (buffers != null)
        {
            list.Transition(buffers.Current, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);
            Labels.Render(list, slot, frameAddress, buffers.Current);
        }

        Island.Render(list, slot, constants, _depth.DepthStencilView);

        if (_color != null && _resolved != null)
        {
            list.Transition(_color, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RESOLVE_SOURCE);
            list.Transition(_resolved, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RESOLVE_DEST);
            native.ResolveSubresource(_resolved.NativeObject, 0, _color.NativeObject, 0, DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB);
        }

        // the cartoon ink follows the entries, so they are resolved for it as they are for picking.
        var pickSource = _entries;
        if (_pickEntries != null && (Picks.HasRequests || _effect == ScreenEffect.Cartoon))
        {
            list.Transition(_entries, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);
            list.Transition(_pickEntries, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            native.SetComputeRootSignature(_resolveRootSignature.NativeObject);
            native.SetPipelineState(_resolvePipeline.NativeObject);
            native.SetComputeRootDescriptorTable(0, _entries.ShaderResourceView.Gpu);
            native.SetComputeRootDescriptorTable(1, _pickEntries.UnorderedAccessViews[0].Gpu);
            native.Dispatch((swapChain.Width + _resolveThreadGroupSize - 1) / _resolveThreadGroupSize, (swapChain.Height + _resolveThreadGroupSize - 1) / _resolveThreadGroupSize, 1);
        }

        if (_pickEntries != null)
        {
            pickSource = _pickEntries;
        }

        // an effect draws the finished scene into the back buffer, otherwise a multisampled scene is copied there as it is.
        Timer.Begin(list, _effectScope);
        if (IsPostEffect(_effect) && _color != null)
        {
            var source = _resolved ?? _color;
            list.Transition(source, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);
            list.Transition(pickSource, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);
            list.Transition(backBuffer, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET);
            _postTargets[0] = backBuffer.RenderTargetView.Cpu;
            list.SetRenderTargets(_postTargets, default);
            list.SetViewport(swapChain.Width, swapChain.Height);
            native.SetGraphicsRootSignature(_postRootSignature.NativeObject);
            native.SetPipelineState(_postPipeline.NativeObject);
            SetPostConstants(native, swapChain.Width, swapChain.Height);
            native.SetGraphicsRootDescriptorTable(1, source.ShaderResourceView.Gpu);
            native.SetGraphicsRootDescriptorTable(2, pickSource.ShaderResourceView.Gpu);
            native.IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            native.DrawInstanced(_postVertexCount, 1, 0, 0);
        }
        else if (_resolved != null)
        {
            list.Transition(_resolved, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE);
            list.Transition(backBuffer, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST);
            native.CopyResource(backBuffer.NativeObject, _resolved.NativeObject);
            list.Transition(backBuffer, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET);
        }
        Timer.End(list, _effectScope);
        Journal.Record(new FrameRecord
        {
            Time = time,
            Slot = slot,
            Width = swapChain.Width,
            Height = swapChain.Height,
            SampleCount = SampleCount,
            InstanceCount = InstanceCount,
            Capacity = Capacity,
            ChunkCount = chunkCount,
            LastVisible = VisibleInstances,
            Animated = animated,
            Drawn = buffers != null && hasInstances,
            LayoutUploaded = layoutUploaded,
            ThumbnailsUploaded = thumbnailsUploaded,
            EntryCellsBytes = Thumbnails.EntryCells.Size,
            IslandInstances = Island.IsVisible ? Island.InstanceCount : 0,
            Picked = Picks.HasRequests,
        });
        Picks.Record(list, slot, pickSource);
        Timer.End(list, _labelsScope);
        Timer.EndFrame(list);
    }

    // the nearest supported count at or below what was asked for, the pipelines and targets are rebuilt for it.
    public void SetSampleCount(uint sampleCount)
    {
        var chosen = SampleCounts.Where(count => count <= Math.Max(1, sampleCount)).DefaultIfEmpty(1u).Max();
        if (chosen == SampleCount)
            return;

        Journal.Note(string.Create(CultureInfo.InvariantCulture, $"sample count {SampleCount} to {chosen}"));
        _device.DirectQueue.WaitForIdle();
        _blocksPipeline.Dispose();
        _groundPipeline.Dispose();
        (_blocksPipeline, _groundPipeline) = CreateScenePipelines(chosen);
        _edgesPipeline?.Dispose();
        _edgesPipeline = null;
        Labels.SetSampleCount(chosen);
        Island.SetSampleCount(chosen);
        SampleCount = chosen;
        DisposeTargets();
    }

    // the edges test depth against the blocks but never write it, and lighten the color target alone.
    private PipelineState CreateEdgesPipeline(uint sampleCount) => PipelineState.CreateGraphics(_device, new GraphicsPipelineDescription
    {
        RootSignature = _edgesRootSignature,
        VertexShader = _edgesVertex,
        PixelShader = _edgesPixel,
        RenderTargetFormats = _sceneFormats,
        DepthStencilFormat = _depthFormat,
        DepthWrite = false,
        CullMode = D3D12_CULL_MODE.D3D12_CULL_MODE_NONE,
        Lighten = true,
        SampleCount = sampleCount,
    });

    private static bool IsPostEffect(ScreenEffect effect) => effect is ScreenEffect.Cartoon or ScreenEffect.PixelArt;

    private static uint LineEffectOf(ScreenEffect effect) => effect switch
    {
        ScreenEffect.Neon => _lineNeon,
        ScreenEffect.NewYork2027 => _lineNewYork,
        _ => 0,
    };

    // the line effects draw on opaque black, a window material does not show through them.
    private Vector4 SceneSky => LineEffectOf(_effect) != 0 ? Vector4.UnitW : SkyColor;

    private static unsafe void SetEdgeConstants(ID3D12GraphicsCommandList list, Vector4 values) => list.SetGraphicsRoot32BitConstants(3, 4, (nint)(&values), 0);

    private (PipelineState Blocks, PipelineState Ground) CreateScenePipelines(uint sampleCount)
    {
        var blocks = PipelineState.CreateGraphics(_device, new GraphicsPipelineDescription
        {
            RootSignature = _blocksRootSignature,
            VertexShader = _blocksVertex,
            PixelShader = _blocksPixel,
            RenderTargetFormats = _sceneFormats,
            DepthStencilFormat = _depthFormat,
            SampleCount = sampleCount,
        });

        var ground = PipelineState.CreateGraphics(_device, new GraphicsPipelineDescription
        {
            RootSignature = _groundRootSignature,
            VertexShader = _groundVertex,
            PixelShader = _groundPixel,
            RenderTargetFormats = _sceneFormats,
            DepthStencilFormat = _depthFormat,
            CullMode = D3D12_CULL_MODE.D3D12_CULL_MODE_NONE,
            SampleCount = sampleCount,
        });
        return (blocks, ground);
    }

    // the number of entries uploaded, minus one when there was no new layout.
    private int UploadPendingLayout(CommandList list, double time)
    {
        var snapshot = _pendingLayout;
        if (snapshot == null)
            return -1;

        _pendingLayout = null;
        var count = (uint)snapshot.Count;
        var bytes = count * (ulong)Unsafe.SizeOf<TargetInstance>();

        // the upload heap and the retired buffers may still be in use by frames in flight.
        _device.DirectQueue.WaitForIdle();
        foreach (var retired in _retired)
        {
            retired.Dispose();
        }
        _retired.Clear();

        // a different tree reuses nothing, its entries have nothing to do with the slots of the previous one.
        var sameTree = Layout != null && ReferenceEquals(Layout.Tree, snapshot.Tree);
        var local = _device.QueryLocalVideoMemory();
        var shared = _device.QueryVideoMemory(DXGI_MEMORY_SEGMENT_GROUP.DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL);
        Journal.Note(string.Create(CultureInfo.InvariantCulture, $"layout of {count} entries, instances {InstanceCount}, capacity {Capacity}, same tree {sameTree}, video {local.CurrentUsage >> 20} of {local.Budget >> 20} MB, shared {shared.CurrentUsage >> 20} of {shared.Budget >> 20} MB, managed {GC.GetTotalMemory(false) >> 20} MB, working set {Environment.WorkingSet >> 20} MB"));
        if (_buffers == null || _buffers.Capacity < count || !sameTree)
        {
            var capacity = Math.Max(_minimumCapacity, (uint)(count * _growthFactor));
            capacity = (capacity + _chunkSize - 1) / _chunkSize * _chunkSize;
            var buffers = new InstanceBuffers(_device, capacity);
            Thumbnails.EnsureCapacity(capacity);
            if (_buffers != null)
            {
                if (sameTree)
                {
                    list.Transition(_buffers.Current, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE);
                    list.Transition(buffers.Current, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST);
                    list.NativeObject.CopyBufferRegion(buffers.Current.NativeObject, 0, _buffers.Current.NativeObject, 0, (ulong)InstanceCount * _currentStride);
                }
                _retired.Add(_buffers);
            }
            _buffers = buffers;
            Journal.Note(string.Create(CultureInfo.InvariantCulture, $"new instance buffers, capacity {capacity}, entry cells {Thumbnails.EntryCells.Size} bytes"));
        }

        if (_upload == null || _upload.Size < bytes)
        {
            _upload?.Dispose();
            _upload = new UploadBuffer(_device, (ulong)(bytes * _growthFactor));
        }

        if (count > 0)
        {
            _upload.Write<TargetInstance>(snapshot.Instances.AsSpan(0, (int)count));
            list.Transition(_buffers.Targets, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST);
            list.NativeObject.CopyBufferRegion(_buffers.Targets.NativeObject, 0, _upload.NativeObject, 0, bytes);
        }

        InstanceCount = count;
        Layout = snapshot;
        _animateUntil = time + _tweenSeconds;
        _settled = false;
        return (int)count;
    }

    private void ReadVisibleCount(uint slot)
    {
        if (!_argumentReadbackPending[slot])
            return;

        Span<D3D12_DRAW_ARGUMENTS> arguments = stackalloc D3D12_DRAW_ARGUMENTS[1];
        _argumentReadbacks[slot].Read(arguments);
        VisibleInstances = arguments[0].InstanceCount;
        // the readback is a few frames old, it is checked against the instance count of the frame that wrote it.
        var instances = _argumentReadbackInstances[slot];
        if (VisibleInstances > instances || arguments[0].VertexCountPerInstance != _boxVertexCount)
        {
            var text = string.Create(CultureInfo.InvariantCulture, $"unexpected draw arguments, {arguments[0].VertexCountPerInstance} vertices, {VisibleInstances} instances of {instances}");
            Journal.Note(text);
            Application.TraceWarning(text);
        }
        _argumentReadbackPending[slot] = false;
    }

    // culls in chunks, sums the chunk counts into offsets and draw arguments, then compacts what survived into one list.
    private void Cull(CommandList list, InstanceBuffers buffers, CullBuffers cull, ulong frameAddress, uint chunkCount)
    {
        var native = list.NativeObject;
        var chunkGroups = (chunkCount + _chunkThreadGroupSize - 1) / _chunkThreadGroupSize;
        list.Transition(buffers.Current, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);
        list.Transition(buffers.Targets, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);
        list.Transition(cull.ChunkLists, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        list.Transition(cull.ChunkCounts, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        native.SetComputeRootSignature(_cullRootSignature.NativeObject);
        native.SetPipelineState(_cullPipeline.NativeObject);
        native.SetComputeRootConstantBufferView(0, frameAddress);
        SetComputeConstants(native, 1, InstanceCount, chunkCount);
        native.SetComputeRootShaderResourceView(2, buffers.Current.GpuVirtualAddress);
        native.SetComputeRootUnorderedAccessView(3, cull.ChunkLists.GpuVirtualAddress);
        native.SetComputeRootUnorderedAccessView(4, cull.ChunkCounts.GpuVirtualAddress);
        native.Dispatch(chunkGroups, 1, 1);

        list.Transition(cull.ChunkCounts, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);
        list.Transition(cull.ChunkOffsets, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        list.Transition(cull.Arguments, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        native.SetComputeRootSignature(_offsetsRootSignature.NativeObject);
        native.SetPipelineState(_offsetsPipeline.NativeObject);
        SetComputeConstants(native, 0, chunkCount, 0);
        native.SetComputeRootShaderResourceView(1, cull.ChunkCounts.GpuVirtualAddress);
        native.SetComputeRootUnorderedAccessView(2, cull.ChunkOffsets.GpuVirtualAddress);
        native.SetComputeRootUnorderedAccessView(3, cull.Arguments.GpuVirtualAddress);
        native.Dispatch(1, 1, 1);

        list.Transition(cull.ChunkLists, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);
        list.Transition(cull.ChunkOffsets, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE);
        list.Transition(cull.Visible, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
        native.SetComputeRootSignature(_compactRootSignature.NativeObject);
        native.SetPipelineState(_compactPipeline.NativeObject);
        SetComputeConstants(native, 0, chunkCount, 0);
        native.SetComputeRootShaderResourceView(1, cull.ChunkLists.GpuVirtualAddress);
        native.SetComputeRootShaderResourceView(2, cull.ChunkCounts.GpuVirtualAddress);
        native.SetComputeRootShaderResourceView(3, cull.ChunkOffsets.GpuVirtualAddress);
        native.SetComputeRootUnorderedAccessView(4, cull.Visible.GpuVirtualAddress);
        native.Dispatch(chunkGroups, 1, 1);
    }

    // the sun looks at the part of the map around the camera target, as wide as the camera sees it in detail plus the longest shadow.
    // its rotation never changes and its window moves by whole texels in steps of a quarter octave, so a still block keeps the same texels.
    private ShadowView ComputeShadowView(Camera camera, LayoutSnapshot layout)
    {
        var height = MathF.Max(layout.BoundsMax.Y, 1);
        var direction = LightDirection;
        var reach = height * new Vector2(direction.X, direction.Z).Length() / MathF.Max(-direction.Y, 0.1f);
        var radius = MathF.Max(camera.Distance * _shadowReach, _minimumShadowRadius) + reach;
        var target = camera.Target;
        var min = new Vector3(MathF.Max(layout.BoundsMin.X, target.X - radius), 0, MathF.Max(layout.BoundsMin.Z, target.Z - radius));
        var max = new Vector3(MathF.Min(layout.BoundsMax.X, target.X + radius), height, MathF.Min(layout.BoundsMax.Z, target.Z + radius));
        if (min.X > max.X || min.Z > max.Z)
        {
            min = new Vector3(layout.BoundsMin.X, 0, layout.BoundsMin.Z);
            max = new Vector3(layout.BoundsMax.X, height, layout.BoundsMax.Z);
        }

        var view = Matrix4x4.CreateLookAt(Vector3.Zero, direction, Vector3.UnitZ);
        var low = new Vector3(float.MaxValue);
        var high = new Vector3(float.MinValue);
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
            var projected = Vector3.Transform(corner, view);
            low = Vector3.Min(low, projected);
            high = Vector3.Max(high, projected);
        }

        var extent = MathF.Pow(2, MathF.Ceiling(MathF.Log2(MathF.Max(MathF.Max(high.X - low.X, high.Y - low.Y), 1)) * _shadowOctaveSteps) / _shadowOctaveSteps);
        var texel = extent / _shadowMapSize;
        var middleX = MathF.Floor((low.X + high.X) * 0.5f / texel) * texel;
        var middleY = MathF.Floor((low.Y + high.Y) * 0.5f / texel) * texel;
        var projection = Camera.CreateReversedOrthographic(middleX - extent * 0.5f, middleX + extent * 0.5f, middleY - extent * 0.5f, middleY + extent * 0.5f, -high.Z - _shadowDepthMargin, -low.Z + _shadowDepthMargin);

        // for culling the sun stands far enough away that every block is about as far from it, so one pixel scale gives sizes in texels.
        var size = (max - min).Length();
        var distance = size * 4 + _shadowDepthMargin;
        var eye = (min + max) * 0.5f - direction * distance;
        return new ShadowView(view * projection, eye, texel, distance / texel);
    }

    private static unsafe void SetComputeConstants(ID3D12GraphicsCommandList list, uint parameter, uint first, uint second)
    {
        var values = stackalloc uint[4];
        values[0] = first;
        values[1] = second;
        values[2] = 0;
        values[3] = 0;
        list.SetComputeRoot32BitConstants(parameter, 4, (nint)values, 0);
    }

    private unsafe void SetPostConstants(ID3D12GraphicsCommandList list, uint width, uint height)
    {
        var island = Island.IsVisible ? Island.Viewport : default;
        var values = stackalloc uint[8];
        values[0] = (uint)_effect;
        values[1] = BitConverter.SingleToUInt32Bits(width);
        values[2] = BitConverter.SingleToUInt32Bits(height);
        values[3] = BitConverter.SingleToUInt32Bits(MathF.Max(1, EffectScale));
        values[4] = BitConverter.SingleToUInt32Bits(island.left);
        values[5] = BitConverter.SingleToUInt32Bits(island.top);
        values[6] = BitConverter.SingleToUInt32Bits(island.right);
        values[7] = BitConverter.SingleToUInt32Bits(island.bottom);
        list.SetGraphicsRoot32BitConstants(0, 8, (nint)values, 0);
    }

    private static unsafe void SetGroundConstants(ID3D12GraphicsCommandList list, Vector4 bounds, bool grid)
    {
        var values = stackalloc uint[5];
        Unsafe.WriteUnaligned((byte*)values, bounds);
        values[4] = grid ? 1u : 0u;
        list.SetGraphicsRoot32BitConstants(1, 5, (nint)values, 0);
    }

    private static unsafe void SetGraphicsConstants(ID3D12GraphicsCommandList list, uint parameter, Vector4 values) => list.SetGraphicsRoot32BitConstants(parameter, 4, (nint)(&values), 0);

    private static (RootSignature RootSignature, PipelineState Pipeline) CreateCompute(GraphicsDevice device, string name)
    {
        var shader = new Shader(name);
        var rootSignature = new RootSignature(device, shader);
        return (rootSignature, PipelineState.CreateCompute(device, rootSignature, shader));
    }

    // a multisampled scene or one an effect draws from renders into a color target of its own rather than into the back buffer.
    private void EnsureTargets(uint width, uint height)
    {
        var multisampled = SampleCount > 1;
        var offscreen = multisampled || IsPostEffect(_effect);
        if (_depth != null && _depth.Width == width && _depth.Height == height && (_color != null) == offscreen)
            return;

        DisposeTargets();
        Journal.Note(string.Create(CultureInfo.InvariantCulture, $"scene targets {width}x{height}, msaa {SampleCount}, effect {_effect}"));
        _entries = new Texture(_device, _heaps, new TextureDescription
        {
            Width = width,
            Height = height,
            Format = _entryFormat,
            SampleCount = SampleCount,
            RenderTarget = true,
            ShaderResource = true,
            InitialState = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET,
        });
        _depth = new Texture(_device, _heaps, new TextureDescription
        {
            Width = width,
            Height = height,
            Format = _depthFormat,
            SampleCount = SampleCount,
            DepthStencil = true,
            ClearDepth = 0,
            InitialState = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_DEPTH_WRITE,
        });

        if (!offscreen)
            return;

        _color = new Texture(_device, _heaps, new TextureDescription
        {
            Width = width,
            Height = height,
            Format = _colorFormat,
            RenderTargetViewFormat = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB,
            ShaderResourceViewFormat = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB,
            SampleCount = SampleCount,
            RenderTarget = true,
            ShaderResource = !multisampled,
            ClearColor = SceneSky,
            InitialState = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET,
        });

        if (!multisampled)
            return;

        // resolved in sRGB so edges blend in light rather than in encoded values, then copied bit for bit into the back buffer or read by an effect.
        _resolved = new Texture(_device, _heaps, new TextureDescription
        {
            Width = width,
            Height = height,
            Format = _colorFormat,
            ShaderResourceViewFormat = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB,
            ShaderResource = true,
            InitialState = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RESOLVE_DEST,
        });
        _pickEntries = new Texture(_device, _heaps, new TextureDescription
        {
            Width = width,
            Height = height,
            Format = _entryFormat,
            UnorderedAccess = true,
            ShaderResource = true,
            InitialState = D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
        });
    }

    private void DisposeTargets()
    {
        _depth?.Dispose();
        _entries?.Dispose();
        _color?.Dispose();
        _resolved?.Dispose();
        _pickEntries?.Dispose();
        _depth = null;
        _entries = null;
        _color = null;
        _resolved = null;
        _pickEntries = null;
    }

    public void Dispose()
    {
        Labels.Dispose();
        Picks.Dispose();
        Thumbnails.Dispose();
        Island.Dispose();
        DisposeTargets();
        _resolvePipeline.Dispose();
        _resolveRootSignature.Dispose();
        _buffers?.Dispose();
        _upload?.Dispose();
        foreach (var retired in _retired)
        {
            retired.Dispose();
        }

        foreach (var readback in _argumentReadbacks)
        {
            readback.Dispose();
        }

        Timer.Dispose();
        _frameConstants.Dispose();
        _shadowConstants.Dispose();
        _postPipeline.Dispose();
        _edgesPipeline?.Dispose();
        _edgesRootSignature.Dispose();
        _postRootSignature.Dispose();
        _shadowPipeline.Dispose();
        _shadowRootSignature.Dispose();
        _shadowMap.Dispose();
        _drawSignature.Dispose();
        _blocksPipeline.Dispose();
        _blocksRootSignature.Dispose();
        _groundPipeline.Dispose();
        _groundRootSignature.Dispose();
        _animatePipeline.Dispose();
        _animateRootSignature.Dispose();
        _cullPipeline.Dispose();
        _cullRootSignature.Dispose();
        _offsetsPipeline.Dispose();
        _offsetsRootSignature.Dispose();
        _compactPipeline.Dispose();
        _compactRootSignature.Dispose();
    }

    private sealed class InstanceBuffers : IDisposable
    {
        public InstanceBuffers(GraphicsDevice device, uint capacity)
        {
            Capacity = capacity;
            Targets = new GpuBuffer(device, null, capacity, (uint)Unsafe.SizeOf<TargetInstance>(), false);
            Current = new GpuBuffer(device, null, capacity, _currentStride, true);
            Camera = new CullBuffers(device, capacity);
            Shadow = new CullBuffers(device, capacity);
        }

        public uint Capacity { get; }
        public GpuBuffer Targets { get; }
        public GpuBuffer Current { get; }
        public CullBuffers Camera { get; }
        public CullBuffers Shadow { get; }

        public void Dispose()
        {
            Targets.Dispose();
            Current.Dispose();
            Camera.Dispose();
            Shadow.Dispose();
        }
    }

    // what one view culls into, the camera has a set and the sun has its own.
    private sealed class CullBuffers : IDisposable
    {
        public CullBuffers(GraphicsDevice device, uint capacity)
        {
            var chunks = capacity / _chunkSize;
            ChunkLists = new GpuBuffer(device, null, chunks * _chunkSize, sizeof(uint), true);
            ChunkCounts = new GpuBuffer(device, null, chunks, sizeof(uint), true);
            ChunkOffsets = new GpuBuffer(device, null, chunks, sizeof(uint), true);
            Visible = new GpuBuffer(device, null, capacity, sizeof(uint), true);
            Arguments = new GpuBuffer(device, null, 8, sizeof(uint), true);
        }

        public GpuBuffer ChunkLists { get; }
        public GpuBuffer ChunkCounts { get; }
        public GpuBuffer ChunkOffsets { get; }
        public GpuBuffer Visible { get; }
        public GpuBuffer Arguments { get; }

        public void Dispose()
        {
            ChunkLists.Dispose();
            ChunkCounts.Dispose();
            ChunkOffsets.Dispose();
            Visible.Dispose();
            Arguments.Dispose();
        }
    }

    private readonly record struct ShadowView(Matrix4x4 ViewProjection, Vector3 Eye, float Texel, float PixelScale);
}
