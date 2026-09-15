namespace Treemapolis;

public sealed class MainWindow : Window
{
    private const uint _frameCount = 3;
    private const DXGI_FORMAT _backBufferFormat = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM;
    private const DXGI_FORMAT _backBufferRenderTargetFormat = DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;
    private const int _defaultClientWidth = 1280;
    private const int _defaultClientHeight = 720;
    private const int _wheelDelta = 120;
    private const double _hudIntervalSeconds = 0.25;
    private const string _hudSeparator = ", ";
    private const string _runAsVerb = "runas";
    private const string _personalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string _appsUseLightTheme = "AppsUseLightTheme";
    private const string _colorSetChanged = "ImmersiveColorSet";
    private const float _panelMargin = 12;
    private const float _tooltipOffset = 18;
    private const float _maximumChromeElapsed = 1 / 30f;
    private const int _saveQuietMilliseconds = 1000;
    private const nuint _xButtonForward = 2;
    private const double _minimumElevation = 0;
    private const double _elevationStep = 25;
    private const double _maximumSunAngle = 355;
    private const double _sunAngleStep = 5;
    private const double _tooltipDelaySeconds = 0.5;
    private const float _islandWidth = 230;
    private const float _framingMargin = 24;
    private const float _islandTextInset = 8;
    private const float _islandNameShare = 0.44f;
    private const float _islandDetailShare = 0.7f;
    private const float _islandImageShare = 0.8f;
    private const float _islandDriveImageSize = 32;
    private const float _islandPlaceImageSize = 20;
    private const float _islandTopMargin = 4;
    private const double _loadingDelaySeconds = 0.25;
    private const float _islandScrollStep = 40;
    private const float _captionTooltipGap = 4;
    private static readonly int[] _thumbnailPixelChoices = [16, 24, 40, 64, 96, 128, 192];
    private static readonly bool _backdropSupported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);
    private static readonly bool _frameAttributesSupported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    private readonly StartupOptions _options;
    private readonly GraphicsDevice _device;
    private readonly DescriptorHeaps _heaps;
    private SwapChain _swapChain;
    private SceneWindow? _sceneWindow;
    private readonly CommandList _commandList;
    private readonly SceneRenderer _renderer;
    private readonly Camera _camera = new();
    private readonly FrameStatistics _statistics = new();
    private readonly NamespaceExplorer _explorer = new();
    private readonly LayoutEngine _layout;
    private readonly Navigator _navigator;
    private readonly List<PickResult> _pickResults = [];
    private readonly ChromeLayer _chrome;
    private readonly Glyphs _glyphs;
    private readonly TitleBar _titleBar = new();
    private readonly HudPanel _performancePanel = new(false);
    private readonly HudPanel _tooltipPanel = new(false);
    private readonly PlaceImages _placeImages = new();
    private readonly LoadingPanel _loadingPanel = new();
    private readonly HudPanel _captionTooltip = new(false);
    private string? _captionTooltipText;
    private double _captionTooltipSince;
    private CameraView? _pendingCamera;
    private bool _restoringLastLocation;
    private readonly LegendPanel _legendPanel = new();
    private readonly SettingsMenu _menu = new();
    private readonly ThumbnailLoader _thumbnails;
    private readonly HashSet<int> _requestedThumbnails = [];
    private readonly PlacesProvider _places;
    private int _placesVersion = -1;
    private readonly SettingsFile _settingsFile;
    private readonly Settings _settings;
    private readonly Timer _saveTimer;
    private bool _savePending;
    private ChromeResources? _chromeResources;
    private Palette _palette = Palette.Dark;
    private bool _chromeDirty = true;
    private bool _chromeAnimating;
    private double _lastChromeSeconds;
    private D2D_POINT_2F _tooltipPosition;
    private FrameCapture? _pendingCapture;
    private LayoutSnapshot? _submittedLayout;
    private NamespaceTree? _framedTree;
    private int _framedRoot = Entry.None;
    private bool _cameraMovedByUser;
    private Matrix4x4 _framedView;
    private DragMode _dragMode;
    private MouseButton _pressedButton;
    private POINT _pressPoint;
    private POINT _lastMouse;
    private bool _suppressNextClick;
    private PickRequest? _lastClick;
    private bool _trackingLeave;
    private RECT _windowedRect;
    private bool _isFullScreen;
    private double _lastTitleUpdate;
    private double _lastHudUpdate = double.MinValue;
    private long _lastHudItems;
    private double _lastHudSeconds;
    private bool _ready;
    private bool _deviceLost;
    private double _loadingSince = -1;
    private int _lastPresentResult;
    private Material _appliedMaterial;

    public event EventHandler? FrameRendered;

    public MainWindow(StartupOptions options)
        : base(Res.WindowTitle, WINDOW_STYLE.WS_OVERLAPPEDWINDOW, _backdropSupported ? WINDOW_EX_STYLE.WS_EX_NOREDIRECTIONBITMAP : 0)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        ValidateOnPaint = false;
        _settingsFile = new SettingsFile(options.SettingsPath, options.FreshSettings);
        _settings = _settingsFile.Load();
        _saveTimer = new Timer(_ => OnSaveElapsed(), null, Timeout.Infinite, Timeout.Infinite);
        _thumbnails = new ThumbnailLoader(() => Invalidate(null, false));
        _places = new PlacesProvider(() => Invalidate(null, false));
        if (!WindowPosition.TryParse(_settings.Window, out var position) || !position.Restore(this))
        {
            ResizeClient(_defaultClientWidth, _defaultClientHeight);
            Center();
        }

        _device = new GraphicsDevice(options.UseWarp, options.Debug, options.GpuValidation);
        _heaps = new DescriptorHeaps(_device);
        _chrome = new ChromeLayer(Handle, options.Debug);
        _swapChain = CreateSwapChain(_backdropSupported && _settings.Material != Material.None);
        _commandList = new CommandList(_device.DirectQueue);
        _renderer = new SceneRenderer(_device, _heaps, _frameCount, _backBufferRenderTargetFormat);
        _layout = new LayoutEngine(_explorer) { ClockEpoch = _statistics.StartTimestamp };
        _navigator = new Navigator(_explorer, _layout, _renderer, _camera, new ShellCommands(Handle));
        _glyphs = new Glyphs(_chrome.Factory);
        _titleBar.Pressed = OnCaptionButton;
        _titleBar.IsElevated = Environment.IsPrivilegedProcess;
        _menu.Changed = OnSettingChanged;
        ApplySettings();
        if (options.Location == null && _settings.LastLocation != null)
        {
            _restoringLastLocation = true;
            _pendingCamera = _settings.Camera;
            _navigator.StartAt(_settings.LastLocation);
        }
        else if (options.Location != null && ShellCommands.ResolveStart(options.Location) is { } start)
        {
            _navigator.StartAt(start);
        }
        else
        {
            _explorer.Open(options.Location);
        }
        _ready = true;
    }

    public GraphicsDevice Device => _device;
    public SwapChain SwapChain => _swapChain;
    public SceneRenderer Renderer => _renderer;
    public FrameStatistics Statistics => _statistics;
    public NamespaceExplorer Explorer => _explorer;
    public LayoutEngine Layout => _layout;
    public Camera Camera => _camera;
    public Navigator Navigator => _navigator;

    // a scripted run needs frames to keep coming even when nothing on screen changes.
    public bool ContinuousRendering { get; set; }

    public bool IsDeviceLost => _deviceLost;
    public bool IsAnimating => !_deviceLost && ContinuousRendering || _explorer.IsBusy || _layout.IsBusy || _renderer.IsAnimating || _camera.IsFlying || _navigator.HasDeferredFlight || _dragMode != DragMode.None || _pendingCapture != null || _chromeAnimating || _navigator.HasPendingLocation || _renderer.Thumbnails.HasPending || _renderer.Island.IsAnimating || _places.Version != _placesVersion || AreChangesShowing || (_captionTooltipText != null && _captionTooltip.Text.Length == 0);
    public ThumbnailLoader Thumbnails => _thumbnails;

    // a change keeps frames coming until its animation has faded.
    private bool AreChangesShowing
    {
        get
        {
            var last = _explorer.Tree.LastChangeTimestamp;
            return last != 0 && Stopwatch.GetTimestamp() - last < (NamespaceTree.ChangeSeconds + NamespaceTree.RemovalSeconds) * Stopwatch.Frequency;
        }
    }
    public TitleBar TitleBar => _titleBar;
    public HudPanel PerformancePanel => _performancePanel;
    public HudPanel TooltipPanel => _tooltipPanel;
    public LegendPanel LegendPanel => _legendPanel;
    public SettingsMenu Menu => _menu;
    public Settings Settings => _settings;
    public string SettingsLocation => _settingsFile.Location;
    public Palette Palette => _palette;

    private float DpiScale => (float)Dpi.width / Constants.USER_DEFAULT_SCREEN_DPI;
    private float ChromeTop => _titleBar.Height;
    private static int FrameThickness => Functions.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXFRAME) + Functions.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXPADDEDBORDER);
    protected override uint AboutSysMenuId => 0;

    protected override Icon? LoadCreationIcon() => Icon.LoadApplicationIcon(32);

    // double clicks are only reported to a class that asks for them.
    protected override void RegisterClass(string className, nint windowProc, Icon? icon = null) =>
        RegisterWindowClass(className, windowProc, WNDCLASS_STYLES.CS_HREDRAW | WNDCLASS_STYLES.CS_VREDRAW | WNDCLASS_STYLES.CS_DBLCLKS, icon);

    // x and y are client pixels, the answer reaches the navigator a few frames later.
    public void RequestPick(int x, int y, PickAction action)
    {
        _renderer.Picks.Request(new PickRequest(x, y, action));
        Invalidate(null, false);
    }

    public void RequestCapture(string filePath)
    {
        _pendingCapture?.Dispose();
        _pendingCapture = new FrameCapture(_device, _swapChain, filePath);
        Invalidate(null, false);
    }

    // until the user takes the camera, it keeps the whole landscape in view as a scan grows it.
    public void FrameAll()
    {
        var layout = _renderer.Layout ?? _submittedLayout;
        if (layout != null && layout.Containers > 0)
        {
            FrameCamera(layout);
        }
        _cameraMovedByUser = false;
        Invalidate(null, false);
    }

    private void SubmitLayout()
    {
        _layout.Update();
        var snapshot = _layout.Latest;
        if (snapshot == null || snapshot == _submittedLayout)
            return;

        _submittedLayout = snapshot;
        _renderer.SetLayout(snapshot);
        _navigator.OnLayoutChanged(snapshot);
        if (snapshot.Containers == 0)
            return;

        // the last session's camera waits until its folder is the map again, then the user is exactly where they left.
        if (_pendingCamera != null && !_navigator.HasPendingLocation && snapshot.Tree == _explorer.Tree && snapshot.Root == _layout.MapRoot)
        {
            _camera.Target = new Vector3(_pendingCamera.TargetX, _pendingCamera.TargetY, _pendingCamera.TargetZ);
            _camera.Yaw = _pendingCamera.Yaw;
            _camera.Pitch = _pendingCamera.Pitch;
            _camera.Distance = _pendingCamera.Distance;
            _pendingCamera = null;
            _restoringLastLocation = false;
            _framedTree = snapshot.Tree;
            _framedRoot = snapshot.Root;
            _cameraMovedByUser = true;
            RememberRecent(snapshot.Tree, snapshot.Root);
            return;
        }

        // a new location is framed at once, a dive or its way back out flies there while the map morphs.
        if (snapshot.Tree != _framedTree)
        {
            _framedTree = snapshot.Tree;
            _framedRoot = snapshot.Root;
            RememberRecent(snapshot.Tree, snapshot.Root);
            _cameraMovedByUser = false;
            FrameCamera(snapshot);
            return;
        }

        if (snapshot.Root != _framedRoot)
        {
            _framedRoot = snapshot.Root;
            RememberRecent(snapshot.Tree, snapshot.Root);
            _camera.FlyToFrame(snapshot.BoundsMin, snapshot.BoundsMax, SceneAspectRatio, GetFramingArea());
            return;
        }

        // the map keeps being framed only while the scan grows it, a layout made for a file that changed later must not move the camera.
        // a camera that is no longer where the framing left it was moved, by a script as much as by the mouse.
        if (_camera.View != _framedView)
        {
            _cameraMovedByUser = true;
        }

        if (!_cameraMovedByUser && _navigator.Selected < 0 && _explorer.IsBusy)
        {
            FrameCamera(snapshot);
        }
    }

    private void FrameCamera(LayoutSnapshot snapshot)
    {
        _camera.Frame(snapshot.BoundsMin, snapshot.BoundsMax, SceneAspectRatio, GetFramingArea());
        _framedView = _camera.View;
    }

    private float IslandRight => (_panelMargin + _islandWidth) * DpiScale;

    private float SceneAspectRatio => _swapChain.Width / (float)Math.Max(1, _swapChain.Height);

    private Vector4 GetFramingArea()
    {
        var width = (float)Math.Max(1, _swapChain.Width);
        var height = (float)Math.Max(1, _swapChain.Height);
        var margin = _framingMargin * DpiScale;
        var left = (_settings.ShowIsland ? IslandRight : 0) + margin;
        var top = ChromeTop + margin;
        return new Vector4(2 * left / width - 1, 2 * margin / height - 1, 1 - 2 * margin / width, 1 - 2 * top / height);
    }

    // once the device is gone nothing it made can be used again, the landscape stops and says why rather than throwing every frame.
    private void RenderFrame()
    {
        if (_deviceLost)
            return;

        try
        {
            RenderFrameCore();
        }
        catch (Exception) when (_device.IsRemoved)
        {
            OnDeviceLost();
        }
    }

    private void OnDeviceLost()
    {
        _deviceLost = true;
        var report = _device.DescribeRemoval();
        Application.TraceError(report);
        Application.TraceError(_renderer.Journal.Describe());
        Text = string.Format(CultureInfo.CurrentCulture, Res.WindowTitleDeviceLost, Res.WindowTitle, _device.RemovedReason);
    }

    private void RenderFrameCore()
    {
        _device.FlushMessages();
        _swapChain.WaitForFrame();
        var frameStart = _statistics.BeginFrame();
        _navigator.Update(_statistics.Seconds);
        OnLastLocationGone();
        UpdateCaptionState();
        _camera.Update(_statistics.Seconds);
        SubmitLayout();
        UpdateThumbnails();
        UpdateIsland();
        HandlePicks();
        UpdateHud();

        _camera.Terrain = _renderer.Layout;
        _renderer.EffectScale = DpiScale;
        _commandList.Begin();
        _renderer.Render(_commandList, _swapChain, _camera, _statistics.Seconds);

        var capture = _pendingCapture;
        _pendingCapture = null;
        if (capture != null)
        {
            _commandList.Transition(_swapChain.CurrentBuffer, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE);
            capture.Record(_commandList, _swapChain);
        }

        _commandList.Transition(_swapChain.CurrentBuffer, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT);
        var fence = _commandList.Execute();
        var chromePixels = RenderChrome(capture != null);
        if (capture != null)
        {
            _device.DirectQueue.WaitForFence(fence);
            capture.Save(chromePixels);
            capture.Dispose();
        }

        _statistics.EndFrame(frameStart);
        var presented = _swapChain.Present(_options.VSync && _settings.VSync);
        if (presented.IsError && _device.IsRemoved)
        {
            OnDeviceLost();
            return;
        }

        // anything but S_OK is said once when it starts, a failing present shows nothing while every frame still renders.
        if (presented.Value != _lastPresentResult)
        {
            _lastPresentResult = presented.Value;
            _renderer.Journal.Note(string.Create(CultureInfo.InvariantCulture, $"present returned 0x{presented.Value:X8}"));
        }
        _swapChain.MoveToNextFrame();
        UpdateTitle();
        FrameRendered?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateIsland()
    {
        var island = _renderer.Island;
        island.IsVisible = _settings.ShowIsland;
        if (_places.Version != _placesVersion)
        {
            _placesVersion = _places.Version;
            island.SetPlaces(_places.Places);
            _placeImages.Clear();
            _chromeDirty = true;
        }

        Functions.GetClientRect(Handle, out var client);
        var scale = DpiScale;
        var margin = _panelMargin * scale;
        var viewport = new D2D_RECT_F { left = margin, top = ChromeTop + _islandTopMargin * scale, right = IslandRight, bottom = client.bottom - margin };
        if (viewport.left != island.Viewport.left || viewport.top != island.Viewport.top || viewport.right != island.Viewport.right || viewport.bottom != island.Viewport.bottom)
        {
            island.Viewport = viewport;
            _chromeDirty = true;
        }

        var tree = _explorer.Tree;
        var mapRoot = _layout.MapRoot;
        island.CurrentLocation = mapRoot >= 0 && mapRoot < tree.Count ? tree.GetFileSystemPath(mapRoot) ?? tree.GetShellNode(mapRoot)?.ParsingName : null;
        var wasAnimating = island.IsAnimating;
        island.Update(_statistics.Seconds, scale);
        _chromeDirty |= wasAnimating || island.IsAnimating;
    }

    // a click on a tile of the island opens its place, the tile jumping while the map goes there.
    public bool PressIslandTile(int tile)
    {
        var place = _renderer.Island.Press(tile, _statistics.Seconds);
        if (place == null)
            return false;

        _renderer.Journal.Note($"island tile {tile} pressed, opening '{place.ParsingName}'");

        if (place.IdList != null)
        {
            _navigator.OpenLocation(place.IdList);
        }
        else
        {
            _navigator.OpenLocation(place.ParsingName);
        }
        _chromeDirty = true;
        Invalidate(null, false);
        return true;
    }

    // the island's names are written by the chrome on the top faces the renderer projected.
    private void RenderIslandLabels(IComObject<ID2D1DeviceContext> context, ChromeResources resources)
    {
        var island = _renderer.Island;
        if (!island.IsVisible)
            return;

        var inset = _islandTextInset * resources.Scale;
        context.Object.PushAxisAlignedClip(island.Viewport, D2D1_ANTIALIAS_MODE.D2D1_ANTIALIAS_MODE_ALIASED);
        foreach (var label in island.Labels)
        {
            var bounds = label.Bounds;

            // a drive writes its name and its free space above the bar along the front of its tile, its picture beside both.
            var height = bounds.bottom - bounds.top;
            var contentBottom = label.Detail == null ? bounds.bottom : bounds.top + height * _islandDetailShare;
            var left = bounds.left + inset;
            var picture = _placeImages.Get(context, label.Place);
            if (picture != null)
            {
                var image = label.Place.Image!;
                var side = MathF.Min((contentBottom - bounds.top) * _islandImageShare, (label.Detail == null ? _islandPlaceImageSize : _islandDriveImageSize) * resources.Scale);
                var fit = side / Math.Max(image.Width, image.Height);
                var width = image.Width * fit;
                var imageHeight = image.Height * fit;
                var imageLeft = MathF.Round(left + (side - width) / 2);
                var imageTop = MathF.Round((bounds.top + contentBottom - imageHeight) / 2);
                var destination = new D2D_RECT_F { left = imageLeft, top = imageTop, right = imageLeft + width, bottom = imageTop + imageHeight };
                unsafe
                {
                    context.Object.DrawBitmap(picture.Object, (nint)(&destination), 1, D2D1_INTERPOLATION_MODE.D2D1_INTERPOLATION_MODE_HIGH_QUALITY_CUBIC, 0, 0);
                }
                left += side + inset;
            }

            var rect = new D2D_RECT_F { left = left, top = bounds.top, right = bounds.right - inset, bottom = label.Detail == null ? bounds.bottom : bounds.top + height * _islandNameShare };
            ChromeResources.DrawText(context, label.Place.DisplayName, resources.CaptionFormat, rect, resources.IslandTextBrush);
            if (label.Detail != null)
            {
                var detail = new D2D_RECT_F { left = rect.left, top = rect.bottom, right = rect.right, bottom = bounds.top + height * _islandDetailShare };
                ChromeResources.DrawText(context, label.Detail, resources.CaptionFormat, detail, resources.IslandDetailBrush);
            }
        }
        context.Object.PopAxisAlignedClip();
    }

    // the pictures the camera is close enough to see are asked of the shell once each, and handed to the atlas as they arrive.
    private void UpdateThumbnails()
    {
        var atlas = _renderer.Thumbnails;
        var tree = _explorer.Tree;
        if (atlas.SetTree(tree))
        {
            _thumbnails.Reset();
            _requestedThumbnails.Clear();
        }

        while (_thumbnails.TryTake(out var thumbnail))
        {
            if (thumbnail.Generation == _thumbnails.Generation)
            {
                atlas.Add(thumbnail);
            }
        }

        var selection = _renderer.Labels.Selection;
        if (!_settings.ShowThumbnails || selection == null || selection.Layout.Tree != tree)
            return;

        foreach (var entry in selection.Thumbnails)
        {
            if (atlas.Touch(entry) || !_requestedThumbnails.Add(entry))
                continue;

            var path = tree.GetFileSystemPath(entry);
            if (path != null)
            {
                _thumbnails.Request(new ThumbnailRequest(entry, _thumbnails.Generation, path));
            }
        }
    }

    private void HandlePicks()
    {
        // whatever sits under a cursor that stays still changes while the camera flies or the layout moves.
        if (_trackingLeave && _dragMode == DragMode.None && (_camera.IsFlying || _renderer.IsTransitioning))
        {
            _renderer.Picks.Request(new PickRequest(_lastMouse.x, _lastMouse.y, PickAction.Hover));
        }

        _pickResults.Clear();
        _renderer.CollectPicks(_swapChain, _pickResults);
        foreach (var result in _pickResults)
        {
            if (result.Request.Action == PickAction.ContextMenu)
            {
                // a context menu runs a modal loop, it waits until this frame is out of the way.
                var entry = result.Entry;
                _ = RunTaskOnUIThread(() => ShowContextMenu(entry), true);
                continue;
            }

            if (result.Request.Action is PickAction.Select or PickAction.Activate)
            {
                _cameraMovedByUser = true;
            }
            _navigator.HandlePick(result, _statistics.Seconds);
        }
        UpdateTooltip();
    }

    private static string DescribePlace(Place place) => place.IsDrive && place.Capacity > 0
        ? string.Format(CultureInfo.CurrentCulture, Res.TooltipDrive, place.DisplayName, Navigator.FormatBytes(place.Capacity - place.FreeSpace), Navigator.FormatBytes(place.Capacity))
        : string.Format(CultureInfo.CurrentCulture, Res.TooltipPlace, place.DisplayName, place.FileSystemPath ?? place.ParsingName);

    private void ShowContextMenu(int entry)
    {
        var location = _navigator.ShowContextMenu(entry);
        if (location != null)
        {
            _navigator.OpenLocation(location);
        }
        Invalidate(null, false);
    }

    // a location that no longer opens, a removed drive or a deleted folder, falls back to This PC.
    private void OnLastLocationGone()
    {
        if (!_restoringLastLocation || _explorer.IsBusy || _navigator.HasPendingLocation)
            return;

        _restoringLastLocation = false;
        if (_explorer.Root >= 0)
            return;

        _pendingCamera = null;
        _explorer.Open((string?)null);
    }

    private void UpdateCaptionState()
    {
        var back = _navigator.CanGoBack;
        var forward = _navigator.CanGoForward;
        var reveal = _navigator.RevealTarget >= 0;
        if (back == _titleBar.BackEnabled && forward == _titleBar.ForwardEnabled && reveal == _titleBar.RevealEnabled)
            return;

        _titleBar.BackEnabled = back;
        _titleBar.ForwardEnabled = forward;
        _titleBar.RevealEnabled = reveal;
        _chromeDirty = true;
    }

    // a folder the map was laid out from is remembered by the name that opens it again, its path, or its shell parsing name.
    private void RememberRecent(NamespaceTree tree, int entry)
    {
        if (entry < 0 || entry >= tree.Count)
            return;

        var parsingName = tree.GetFileSystemPath(entry) ?? tree.GetShellNode(entry)?.ParsingName;
        if (parsingName == null)
            return;

        SettingsFile.RememberFolder(_settings, parsingName, tree.GetName(entry).ToString());
        ScheduleSave();
    }

    private List<MenuEntry> BuildRecentEntries()
    {
        var entries = new List<MenuEntry>();
        foreach (var folder in _settings.RecentFolders)
        {
            var target = folder.ParsingName;
            entries.Add(new MenuEntry
            {
                Label = Path.IsPathFullyQualified(target) ? target : folder.ToString(),
                ClosesMenu = true,
                Invoked = () => _navigator.OpenLocation(target),
            });
        }

        if (entries.Count == 0)
        {
            entries.Add(new MenuEntry { Label = Res.RecentNothing, Enabled = () => false });
            return entries;
        }

        entries.Add(MenuEntry.Separator);
        entries.Add(new MenuEntry
        {
            Label = Res.RecentRemoveMissing,
            Invoked = () => _ = RemoveMissingRecentAsync(),
        });
        entries.Add(new MenuEntry
        {
            Label = Res.RecentClear,
            Invoked = _settings.RecentFolders.Clear,
        });
        return entries;
    }

    // whether a folder is still there is a question for the shell, asked away from the UI thread.
    private async Task RemoveMissingRecentAsync()
    {
        var names = _settings.RecentFolders.Select(folder => folder.ParsingName).ToArray();
        var missing = await Task.Run(() =>
        {
            var gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                using var item = ShellN.Extensions.ShellItem.FromParsingName(name, throwOnError: false);
                if (item == null)
                {
                    gone.Add(name);
                }
            }
            return gone;
        }).ConfigureAwait(true);

        if (_settings.RecentFolders.RemoveAll(folder => missing.Contains(folder.ParsingName)) > 0)
        {
            _menu.Refresh();
            OnSettingChanged();
        }
    }

    private void UpdateTooltip()
    {
        var place = _renderer.Island.HoveredPlace;
        var text = _dragMode != DragMode.None ? null : place != null ? DescribePlace(place) : _navigator.DescribeHovered();
        var point = place != null ? new Vector2(_lastMouse.x, _lastMouse.y) : _navigator.HoverPoint;
        var position = new D2D_POINT_2F(point.X, point.Y);
        _chromeDirty |= _tooltipPanel.SetText(text);
        if (text != null && (position.x != _tooltipPosition.x || position.y != _tooltipPosition.y))
        {
            _tooltipPosition = position;
            _chromeDirty = true;
        }
    }

    // the chrome is drawn only when something in it changed or is still fading, a scene frame alone never draws it.
    private byte[]? RenderChrome(bool readBack)
    {
        if (!_chromeDirty && !_chromeAnimating && !readBack)
            return null;

        var resources = EnsureChromeResources();
        var seconds = _statistics.Seconds;
        resources.ElapsedSeconds = (float)Math.Clamp(seconds - _lastChromeSeconds, 0, _maximumChromeElapsed);
        _lastChromeSeconds = seconds;
        resources.Animating = false;
        _chromeDirty = false;

        Functions.GetClientRect(Handle, out var client);
        var bounds = new D2D_RECT_F { left = 0, top = 0, right = client.right, bottom = client.bottom };
        _titleBar.Update(bounds, DpiScale);
        _titleBar.IsMaximized = IsZoomed || _isFullScreen;
        _titleBar.IsFullScreen = _isFullScreen;
        var pixels = _chrome.Draw(context =>
        {
            _titleBar.Render(context, resources);

            var margin = _panelMargin * resources.Scale;
            RenderIslandLabels(context, resources);

            // the middle of the map, right of the island and under the caption.
            var mapLeft = _renderer.Island.IsVisible ? _renderer.Island.Viewport.right : 0;
            _loadingPanel.Render(context, resources, new D2D_POINT_2F((mapLeft + bounds.right) / 2, (ChromeTop + bounds.bottom) / 2), seconds);

            var performance = _performancePanel.Measure(resources);
            _performancePanel.Render(context, resources, new D2D_POINT_2F(bounds.right - margin - performance.width, ChromeTop + margin));

            // below and right of the cursor, pushed back inside the window near its edges.
            var tooltip = _tooltipPanel.Measure(resources);
            var offset = _tooltipOffset * resources.Scale;
            var x = Math.Clamp(_tooltipPosition.x + offset, 0, MathF.Max(0, bounds.right - tooltip.width));
            var y = _tooltipPosition.y + offset + tooltip.height > bounds.bottom ? _tooltipPosition.y - offset - tooltip.height : _tooltipPosition.y + offset;
            _tooltipPanel.Render(context, resources, new D2D_POINT_2F(x, y));

            _legendPanel.SetColorMode(_layout.ColorMode);
            var legend = _legendPanel.Measure(resources);
            _legendPanel.Render(context, resources, new D2D_POINT_2F(bounds.right - margin - legend.width, bounds.bottom - margin - legend.height));
            RenderCaptionTooltip(context, resources, bounds);
            _menu.Render(context, resources);
        }, readBack);
        _chromeAnimating = resources.Animating;
        return pixels;
    }

    // a caption button says what it does once the pointer has rested on it, under the button and inside the window.
    private void RenderCaptionTooltip(IComObject<ID2D1DeviceContext> context, ChromeResources resources, D2D_RECT_F bounds)
    {
        var text = _menu.IsOpen ? null : _titleBar.TooltipText;
        if (text != _captionTooltipText)
        {
            _captionTooltipText = text;
            _captionTooltipSince = _statistics.Seconds;
            _captionTooltip.SetText(null);
        }

        if (text == null)
            return;

        if (_statistics.Seconds - _captionTooltipSince < _tooltipDelaySeconds)
        {
            resources.Animating = true;
            return;
        }

        _captionTooltip.SetText(text);
        var size = _captionTooltip.Measure(resources);
        var anchor = _titleBar.TooltipAnchor;
        var x = Math.Clamp((anchor.left + anchor.right - size.width) / 2, 0, MathF.Max(0, bounds.right - size.width));
        _captionTooltip.Render(context, resources, new D2D_POINT_2F(x, anchor.bottom + _captionTooltipGap * resources.Scale));
    }

    private ChromeResources EnsureChromeResources()
    {
        var scale = DpiScale;
        var onMaterial = _swapChain.IsComposition;
        if (_chromeResources != null && _chromeResources.Scale == scale && _chromeResources.Palette == _palette && _chromeResources.OnMaterial == onMaterial)
            return _chromeResources;

        _chromeResources?.Dispose();
        _chromeResources = new ChromeResources(_chrome.DeviceContext, _chrome.Factory, _glyphs, _palette, onMaterial, scale);
        return _chromeResources;
    }

    private static bool SystemIsDark()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_personalizeKey);
        return key?.GetValue(_appsUseLightTheme) is not int light || light == 0;
    }

    private void ApplySettings()
    {
        _layout.Options = new LayoutOptions(_settings.ColorMode, _settings.ShowHidden, (float)(Math.Clamp(_settings.Elevation, _minimumElevation, Settings.MaximumElevation) / 100));
        _titleBar.ShowHidden = _settings.ShowHidden;
        _renderer.SetSampleCount((uint)Math.Max(1, _settings.Antialias));
        _performancePanel.IsVisible = _settings.ShowPerformance;
        _legendPanel.IsVisible = _settings.ShowLegend;
        _renderer.Labels.IsVisible = _settings.ShowLabels;
        _renderer.Labels.ShowThumbnails = _settings.ShowThumbnails;
        _renderer.ShowThumbnails = _settings.ShowThumbnails;
        _renderer.ShowShadows = _settings.ShowShadows;
        _renderer.Effect = _settings.Effect;
        _renderer.SunAngle = (float)_settings.SunAngle;
        _renderer.Ground = _settings.Ground;
        _explorer.WatchChanges = _settings.WatchChanges;
        _explorer.ScanDrives = _settings.ScanDrives;
        _renderer.Labels.MinimumThumbnailPixels = Math.Max(1, _settings.MinimumThumbnailPixels);
        ApplyTheme();
    }

    private unsafe void ApplyTheme()
    {
        var dark = _settings.Appearance == Appearance.Dark || (_settings.Appearance == Appearance.System && SystemIsDark());
        _palette = dark ? Palette.Dark : Palette.Light;
        if (_frameAttributesSupported)
        {
            var value = dark ? 1 : 0;
            Functions.DwmSetWindowAttribute(Handle, (uint)DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, (nint)(&value), sizeof(int));
        }
        ApplyMaterial();
    }

    // Mica and Acrylic show through a transparent sky, which needs the scene in a composition swap chain under the chrome.
    // full screen has nothing behind it to show, and the system backdrop of a window covering the monitor flickers,
    // so there the backdrop is off and the sky is opaque, while the swap chain stays what it was.
    private unsafe void ApplyMaterial()
    {
        var material = _backdropSupported ? _settings.Material : Material.None;
        if (_backdropSupported)
        {
            var type = (int)((_isFullScreen ? Material.None : material) switch
            {
                Material.Mica => DWM_SYSTEMBACKDROP_TYPE.DWMSBT_MAINWINDOW,
                Material.Acrylic => DWM_SYSTEMBACKDROP_TYPE.DWMSBT_TRANSIENTWINDOW,
                _ => DWM_SYSTEMBACKDROP_TYPE.DWMSBT_NONE,
            });
            Functions.DwmSetWindowAttribute(Handle, (uint)DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, (nint)(&type), sizeof(int));

            // the system draws a backdrop only under the frame, so the frame is extended over the whole client area while there is one.
            var extent = material == Material.None ? 0 : -1;
            Functions.DwmExtendFrameIntoClientArea(Handle, new MARGINS { cxLeftWidth = extent, cxRightWidth = extent, cyTopHeight = extent, cyBottomHeight = extent }).ThrowOnError();

            // over an extended frame the system draws its own caption buttons under ours, unless the window has no system menu.
            var current = Style;
            var style = material == Material.None ? current | WINDOW_STYLE.WS_SYSMENU : current & ~WINDOW_STYLE.WS_SYSMENU;
            if (style != current)
            {
                Style = style;
                SetWindowPos(HWND.Null, 0, 0, 0, 0, SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
            }
        }

        // a composition swap chain stops showing when the backdrop under it changes kind, so every new material gets a new one.
        var composition = material != Material.None;
        if (composition != _swapChain.IsComposition || (composition && material != _appliedMaterial))
        {
            _device.WaitForIdle();
            _chrome.SetScene(0);
            _swapChain.Dispose();
            _swapChain = CreateSwapChain(composition);
        }

        _appliedMaterial = material;
        _renderer.SkyColor = composition && !_isFullScreen ? Vector4.Zero : new Vector4(_palette.Sky, 1);
        _renderer.FogScale = _palette.Fog;
        _chromeDirty = true;
        Invalidate(null, false);
    }

    // the composition swap chain is shown by a visual of the main window, the tearing one presents to a child window of its own.
    private SwapChain CreateSwapChain(bool composition)
    {
        Functions.GetClientRect(Handle, out var client);
        if (composition)
        {
            _sceneWindow?.Dispose();
            _sceneWindow = null;
            var swapChain = new SwapChain(_device, _heaps.RenderTargets, Handle, _frameCount, _backBufferFormat, _backBufferRenderTargetFormat, true);
            swapChain.Resize((uint)client.Width, (uint)client.Height);
            _chrome.SetScene(swapChain.ComObject.ToComInstanceNoAddRef());
            return swapChain;
        }

        _sceneWindow ??= new SceneWindow(Handle, client);
        return new SwapChain(_device, _heaps.RenderTargets, _sceneWindow.Handle, _frameCount, _backBufferFormat, _backBufferRenderTargetFormat, false);
    }

    private void OnSettingChanged()
    {
        _chromeDirty = true;
        ScheduleSave();
        Invalidate(null, false);
    }

    private void ScheduleSave()
    {
        _savePending = true;
        _saveTimer.Change(_saveQuietMilliseconds, Timeout.Infinite);
    }

    private void OnSaveElapsed()
    {
        try
        {
            _ = RunTaskOnUIThread(SaveSettings);
        }
        catch (Exception ex)
        {
            Application.TraceVerbose($"a settings save was dropped: {ex.Message}");
        }
    }

    private void SaveSettings()
    {
        if (!_savePending || IsDisposingOrDisposed)
            return;

        _savePending = false;
        CapturePosition();
        _settingsFile.SaveLater(_settings);
    }

    private void CapturePosition()
    {
        if (_isFullScreen)
            return;

        var position = WindowPosition.Get(this);
        if (position != null)
        {
            _settings.Window = position.Value.ToString();
        }

        // while the last session is still being reopened, what it saved is still where the user was.
        if (_restoringLastLocation)
            return;

        _settings.LastLocation = _navigator.CurrentLocation;
        _settings.Camera = new CameraView
        {
            TargetX = _camera.Target.X,
            TargetY = _camera.Target.Y,
            TargetZ = _camera.Target.Z,
            Yaw = _camera.Yaw,
            Pitch = _camera.Pitch,
            Distance = _camera.Distance,
        };
    }

    // what a batch run changes, by the name the settings file uses.
    public bool SetSetting(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        switch (name)
        {
            case nameof(Settings.Appearance) when Enum.TryParse<Appearance>(value, true, out var appearance):
                _settings.Appearance = appearance;
                ApplyTheme();
                break;

            case nameof(Settings.Material) when Enum.TryParse<Material>(value, true, out var material):
                _settings.Material = material;
                ApplyMaterial();
                break;

            case nameof(Settings.ColorMode) when Enum.TryParse<ColorMode>(value, true, out var colorMode):
                _settings.ColorMode = colorMode;
                ApplySettings();
                break;

            case nameof(Settings.Antialias) when int.TryParse(value, CultureInfo.InvariantCulture, out var antialias):
                _settings.Antialias = antialias;
                ApplySettings();
                break;

            case nameof(Settings.VSync) when bool.TryParse(value, out var vsync):
                _settings.VSync = vsync;
                break;

            case nameof(Settings.ShowStatus) when bool.TryParse(value, out var status):
                _settings.ShowStatus = status;
                ApplySettings();
                break;

            case nameof(Settings.ShowPerformance) when bool.TryParse(value, out var performance):
                _settings.ShowPerformance = performance;
                ApplySettings();
                break;

            case nameof(Settings.ShowLegend) when bool.TryParse(value, out var legend):
                _settings.ShowLegend = legend;
                ApplySettings();
                break;

            case nameof(Settings.MinimumThumbnailPixels) when int.TryParse(value, CultureInfo.InvariantCulture, out var thumbnailPixels):
                _settings.MinimumThumbnailPixels = thumbnailPixels;
                ApplySettings();
                break;

            case nameof(Settings.WatchChanges) when bool.TryParse(value, out var watch):
                _settings.WatchChanges = watch;
                ApplySettings();
                break;

            case nameof(Settings.ScanDrives) when bool.TryParse(value, out var scanDrives):
                _settings.ScanDrives = scanDrives;
                ApplySettings();
                break;

            case nameof(Settings.ShowIsland) when bool.TryParse(value, out var island):
                _settings.ShowIsland = island;
                ApplySettings();
                break;

            case nameof(Settings.ShowHidden) when bool.TryParse(value, out var hidden):
                _settings.ShowHidden = hidden;
                ApplySettings();
                break;

            case nameof(Settings.SunAngle) when double.TryParse(value, CultureInfo.InvariantCulture, out var sunAngle):
                _settings.SunAngle = sunAngle;
                ApplySettings();
                break;

            case nameof(Settings.Elevation) when double.TryParse(value, CultureInfo.InvariantCulture, out var elevation):
                _settings.Elevation = elevation;
                ApplySettings();
                break;

            case nameof(Settings.Effect) when Enum.TryParse<ScreenEffect>(value, true, out var effect):
                _settings.Effect = effect;
                ApplySettings();
                break;

            case nameof(Settings.Ground) when Enum.TryParse<Ground>(value, true, out var ground):
                _settings.Ground = ground;
                ApplySettings();
                break;

            case nameof(Settings.ShowShadows) when bool.TryParse(value, out var shadows):
                _settings.ShowShadows = shadows;
                break;

            case nameof(Settings.ShowThumbnails) when bool.TryParse(value, out var images):
                _settings.ShowThumbnails = images;
                ApplySettings();
                break;

            case nameof(Settings.ShowLabels) when bool.TryParse(value, out var labels):
                _settings.ShowLabels = labels;
                ApplySettings();
                break;

            default:
                return false;
        }
        OnSettingChanged();
        return true;
    }

    public void OpenSettingsMenu()
    {
        var resources = EnsureChromeResources();
        Functions.GetClientRect(Handle, out var client);
        var frame = new D2D_RECT_F { left = 0, top = ChromeTop, right = client.right, bottom = client.bottom };
        _menu.Open(BuildSettingsEntries(), _titleBar.GearBounds, frame, resources);
        RequestPick(-1, -1, PickAction.Hover);
        _chromeDirty = true;
        Invalidate(null, false);
    }

    private List<MenuEntry> BuildSettingsEntries() =>
    [
        new MenuEntry
        {
            Label = Res.SettingTheme,
            Kind = MenuEntryKind.Submenu,
            Value = () => NameOf(_settings.Appearance),
            Children = () => ChoiceEntries(Enum.GetValues<Appearance>(), NameOf, () => _settings.Appearance, value =>
            {
                _settings.Appearance = value;
                ApplyTheme();
            }),
        },
        new MenuEntry
        {
            Label = Res.SettingMaterial,
            Kind = MenuEntryKind.Submenu,
            Enabled = () => _backdropSupported,
            Value = () => NameOf(_settings.Material),
            Children = () => ChoiceEntries(Enum.GetValues<Material>(), NameOf, () => _settings.Material, value =>
            {
                _settings.Material = value;
                ApplyMaterial();
            }),
        },
        new MenuEntry
        {
            Label = Res.SettingColors,
            Kind = MenuEntryKind.Submenu,
            Value = () => NameOf(_layout.ColorMode),
            Children = () => ChoiceEntries(Enum.GetValues<ColorMode>(), NameOf, () => _layout.ColorMode, value =>
            {
                _settings.ColorMode = value;
                ApplySettings();
            }),
        },
        MenuEntry.Separator,
        ToggleEntry(Res.SettingShowStatus, () => _settings.ShowStatus, value => _settings.ShowStatus = value),
        ToggleEntry(Res.SettingShowPerformance, () => _settings.ShowPerformance, value => _settings.ShowPerformance = value),
        ToggleEntry(Res.SettingShowLegend, () => _settings.ShowLegend, value => _settings.ShowLegend = value),
        ToggleEntry(Res.SettingShowLabels, () => _settings.ShowLabels, value => _settings.ShowLabels = value),
        ToggleEntry(Res.SettingShowHidden, () => _settings.ShowHidden, value => _settings.ShowHidden = value),
        ToggleEntry(Res.SettingShowIsland, () => _settings.ShowIsland, value => _settings.ShowIsland = value),
        ToggleEntry(Res.SettingWatchChanges, () => _settings.WatchChanges, value => _settings.WatchChanges = value),
        ToggleEntry(Res.SettingScanDrives, () => _settings.ScanDrives, value => _settings.ScanDrives = value),
        new MenuEntry
        {
            Label = Res.SettingElevation,
            Kind = MenuEntryKind.Slider,
            Minimum = _minimumElevation,
            Maximum = Settings.MaximumElevation,
            Step = _elevationStep,
            Number = () => _settings.Elevation,
            Value = () => string.Format(CultureInfo.CurrentCulture, Res.ElevationPercent, _settings.Elevation),
            SetNumber = value =>
            {
                _settings.Elevation = value;
                ApplySettings();
            },
        },
        new MenuEntry
        {
            Label = Res.SettingGround,
            Kind = MenuEntryKind.Submenu,
            Value = () => NameOf(_settings.Ground),
            Children = () => ChoiceEntries(Enum.GetValues<Ground>(), NameOf, () => _settings.Ground, value =>
            {
                _settings.Ground = value;
                ApplySettings();
            }),
        },
        new MenuEntry
        {
            Label = Res.SettingEffect,
            Kind = MenuEntryKind.Submenu,
            Value = () => NameOf(_settings.Effect),
            Children = () => ChoiceEntries(Enum.GetValues<ScreenEffect>(), NameOf, () => _settings.Effect, value =>
            {
                _settings.Effect = value;
                ApplySettings();
            }),
        },
        ToggleEntry(Res.SettingShowShadows, () => _settings.ShowShadows, value => _settings.ShowShadows = value),
        new MenuEntry
        {
            Label = Res.SettingSunAngle,
            Kind = MenuEntryKind.Slider,
            Minimum = 0,
            Maximum = _maximumSunAngle,
            Step = _sunAngleStep,
            Number = () => _settings.SunAngle,
            Value = () => string.Format(CultureInfo.CurrentCulture, Res.SunAngleDegrees, _settings.SunAngle),
            SetNumber = value =>
            {
                _settings.SunAngle = value;
                ApplySettings();
            },
        },
        ToggleEntry(Res.SettingShowThumbnails, () => _settings.ShowThumbnails, value => _settings.ShowThumbnails = value),
        new MenuEntry
        {
            Label = Res.SettingThumbnailPixels,
            Kind = MenuEntryKind.Submenu,
            Enabled = () => _settings.ShowThumbnails,
            Value = () => NameOfPixels(_settings.MinimumThumbnailPixels),
            Children = () => ChoiceEntries(_thumbnailPixelChoices, NameOfPixels, () => _settings.MinimumThumbnailPixels, value =>
            {
                _settings.MinimumThumbnailPixels = value;
                ApplySettings();
            }),
        },
        ToggleEntry(Res.SettingVSync, () => _settings.VSync, value => _settings.VSync = value),
        new MenuEntry
        {
            Label = Res.SettingAntialias,
            Kind = MenuEntryKind.Submenu,
            Value = () => NameOfSampleCount(_renderer.SampleCount),
            Children = () => ChoiceEntries([.. _renderer.SampleCounts], NameOfSampleCount, () => _renderer.SampleCount, value =>
            {
                _settings.Antialias = (int)value;
                ApplySettings();
            }),
        },
        MenuEntry.Separator,
        new MenuEntry
        {
            Label = Res.SettingRecentFolders,
            Kind = MenuEntryKind.Submenu,
            Children = BuildRecentEntries,
        },
        new MenuEntry
        {
            Label = Res.SettingFile,
            ClosesMenu = true,
            Invoked = () =>
            {
                SaveSettingsNow();
                ShellCommands.Reveal(_settingsFile.Location);
            },
        },
    ];

    private void SaveSettingsNow()
    {
        _savePending = false;
        CapturePosition();
        _settingsFile.Save(_settings);
    }

    private MenuEntry ToggleEntry(string label, Func<bool> get, Action<bool> set) => new()
    {
        Label = label,
        Kind = MenuEntryKind.Toggle,
        Checked = get,
        Invoked = () =>
        {
            set(!get());
            ApplySettings();
        },
    };

    private static string NameOfPixels(int pixels) => string.Format(CultureInfo.CurrentCulture, Res.ThumbnailPixels, pixels);

    private static string NameOfSampleCount(uint count) => count <= 1 ? Res.AntialiasOff : string.Format(CultureInfo.CurrentCulture, Res.AntialiasSamples, count);

    private static List<MenuEntry> ChoiceEntries<T>(T[] values, Func<T, string> name, Func<T> get, Action<T> set) where T : struct
    {
        var entries = new List<MenuEntry>();
        foreach (var value in values)
        {
            var chosen = value;
            entries.Add(new MenuEntry
            {
                Label = name(chosen),
                Kind = MenuEntryKind.Toggle,
                Checked = () => EqualityComparer<T>.Default.Equals(get(), chosen),
                Invoked = () => set(chosen),
            });
        }
        return entries;
    }

    private static string NameOf(Appearance appearance) => appearance switch
    {
        Appearance.Dark => Res.ThemeDark,
        Appearance.Light => Res.ThemeLight,
        _ => Res.ThemeSystem,
    };

    private static string NameOf(Material material) => material switch
    {
        Material.Mica => Res.MaterialMica,
        Material.Acrylic => Res.MaterialAcrylic,
        _ => Res.MaterialNone,
    };

    private static string NameOf(ScreenEffect effect) => effect switch
    {
        ScreenEffect.Cartoon => Res.EffectCartoon,
        ScreenEffect.PixelArt => Res.EffectPixelArt,
        ScreenEffect.Neon => Res.EffectNeon,
        ScreenEffect.NewYork2027 => Res.EffectNewYork,
        _ => Res.EffectNone,
    };

    private static string NameOf(Ground ground) => ground switch
    {
        Ground.Plain => Res.GroundPlain,
        Ground.None => Res.GroundNone,
        _ => Res.GroundGrid,
    };

    private static string NameOf(ColorMode colorMode) => colorMode == ColorMode.Age ? Res.ColorModeAge : Res.ColorModeType;

    private void OnCaptionButton(CaptionButton button)
    {
        switch (button)
        {
            case CaptionButton.Back:
                _navigator.GoBack();
                break;

            case CaptionButton.Forward:
                _navigator.GoForward();
                break;

            case CaptionButton.Reveal:
                _navigator.Reveal();
                break;

            case CaptionButton.Up:
                _cameraMovedByUser = true;
                _navigator.GoToParent();
                break;

            case CaptionButton.Hidden:
                ToggleHidden();
                break;

            case CaptionButton.FrameAll:
                _navigator.ClearSelection();
                FrameAll();
                break;

            case CaptionButton.ColorMode:
                ToggleColorMode();
                break;

            case CaptionButton.Elevate:
                RestartAsAdministrator();
                break;

            case CaptionButton.Settings:
                OpenSettingsMenu();
                break;
        }
        Invalidate(null, false);
    }

    private LRESULT HitTest(HWND hwnd, WPARAM wParam, LPARAM lParam)
    {
        var point = ToPoint(lParam);
        Functions.ScreenToClient(hwnd, ref point);

        // full screen keeps the caption and its window buttons, but nothing drags or resizes the window.
        if (_isFullScreen)
        {
            var hit = _titleBar.HitTest(point.x, point.y);
            return new LRESULT { Value = hit == TitleBar.HitCaption ? TitleBar.HitClient : hit };
        }

        var result = DefWindowProc(hwnd, MessageDecoder.WM_NCHITTEST, wParam, lParam);
        if (result.Value != TitleBar.HitClient && result.Value != TitleBar.HitCaption)
            return result;

        if (!IsZoomed && point.y < FrameThickness)
            return new LRESULT { Value = TitleBar.HitTop };

        return new LRESULT { Value = _titleBar.HitTest(point.x, point.y) };
    }

    private void SetHotWindowButton(int hitTest)
    {
        var hot = hitTest is TitleBar.HitMinimize or TitleBar.HitMaximize or TitleBar.HitClose ? hitTest : 0;
        if (_titleBar.HotWindowButton == hot)
            return;

        _titleBar.HotWindowButton = hot;
        _chromeDirty = true;
        Invalidate(null, false);
    }

    private void UpdateHud()
    {
        var seconds = _statistics.Seconds;
        if (seconds - _lastHudUpdate < _hudIntervalSeconds)
            return;

        var tree = _explorer.Tree;
        var root = _explorer.Root;
        var items = tree.Count;
        var rate = seconds > _lastHudSeconds ? (items - _lastHudItems) / (seconds - _lastHudSeconds) : 0;
        _lastHudItems = items;
        _lastHudSeconds = seconds;
        _lastHudUpdate = seconds;

        var status = new StringBuilder();
        if (root >= 0)
        {
            // where the dive went, from the scanned root down to the folder the map is laid out from.
            var title = new StringBuilder();
            var mapRoot = _layout.MapRoot;
            var crumbs = new List<int>();
            for (var current = mapRoot >= 0 && mapRoot < tree.Count ? mapRoot : root; current != Entry.None && current != root; current = tree[current].Parent)
            {
                crumbs.Add(current);
            }

            title.Append(tree.GetName(root));
            for (var i = crumbs.Count - 1; i >= 0; i--)
            {
                title.Append(Res.HudBreadcrumbSeparator).Append(tree.GetName(crumbs[i]));
            }

            _titleBar.UpEnabled = true;
            if (_titleBar.Title != title.ToString())
            {
                _titleBar.Title = title.ToString();
                _chromeDirty = true;
            }

            if (crumbs.Count > 0)
            {
                root = crumbs[0];
            }

        }

        var size = root >= 0 ? Navigator.FormatBytes(tree[root].TotalSize) : string.Empty;
        status.Append(_explorer.IsBusy
            ? string.Format(CultureInfo.CurrentCulture, Res.HudScanning, items, Math.Max(0, rate), size)
            : string.Format(CultureInfo.CurrentCulture, Res.HudItems, items, size));

        var mft = _explorer.MasterFileTable;
        if (mft != null)
        {
            status.Append(_hudSeparator).Append(mft.RecordsRead < mft.RecordCount
                ? string.Format(CultureInfo.CurrentCulture, Res.HudMasterFileTableReading, _explorer.MasterFileTableDrive, mft.RecordsRead, mft.RecordCount)
                : string.Format(CultureInfo.CurrentCulture, Res.HudMasterFileTableRead, _explorer.MasterFileTableDrive, mft.RecordCount));
        }
        else if (_explorer.LastError != null)
        {
            status.Append(_hudSeparator).Append(string.Format(CultureInfo.CurrentCulture, Res.HudScanError, _explorer.LastError.Message));
        }
        else if (_explorer.WalkedDrive != null)
        {
            status.Append(_hudSeparator).Append(string.Format(CultureInfo.CurrentCulture, Res.HudWalked, _explorer.WalkedDrive));
        }
        UpdateLoading(seconds);
        var statusText = _settings.ShowStatus ? status.ToString() : string.Empty;
        if (_titleBar.Status != statusText)
        {
            _titleBar.Status = statusText;
            _chromeDirty = true;
        }

        var timer = _renderer.Timer;
        var passes = new StringBuilder();
        for (var i = 0; i < timer.Names.Count; i++)
        {
            if (i > 0)
            {
                passes.Append(_hudSeparator);
            }
            passes.Append(string.Format(CultureInfo.CurrentCulture, Res.HudPass, timer.Names[i], timer.GetMilliseconds(i)));
        }

        var layout = _renderer.Layout;
        var features = _device.Features;
        var performance = new StringBuilder()
            .Append(string.Format(CultureInfo.CurrentCulture, Res.HudFrame, _statistics.FramesPerSecond, _statistics.CpuMilliseconds))
            .AppendLine(_renderer.SampleCount > 1 ? _hudSeparator + string.Format(CultureInfo.CurrentCulture, Res.HudSamples, _renderer.SampleCount) : string.Empty)
            .AppendLine(string.Format(CultureInfo.CurrentCulture, Res.HudGpu, passes))
            .AppendLine(string.Format(CultureInfo.CurrentCulture, Res.HudInstances, _renderer.InstanceCount, _renderer.VisibleInstances, _renderer.Labels.LabelCount))
            .AppendLine(string.Format(CultureInfo.CurrentCulture, Res.HudLayout, layout?.BuildMilliseconds ?? 0, layout?.Containers ?? 0, layout?.Files ?? 0, _device.QueryLocalVideoMemory().CurrentUsage >> 20))
            .Append(string.Format(CultureInfo.CurrentCulture, Res.HudAdapter, _device.AdapterName, features.ShaderModelVersion, features.RaytracingVersion ?? Res.HudNotSupported, features.MeshShaderVersion ?? Res.HudNotSupported));
        _chromeDirty |= _performancePanel.SetText(performance.ToString());
    }

    // until the first items are laid out the map is empty, so the panel says what is being read, after a short delay so a quick scan never flashes it.
    private void UpdateLoading(double seconds)
    {
        var layout = _renderer.Layout;
        var waiting = _explorer.IsBusy && (layout == null || layout.Count <= 1);
        if (!waiting)
        {
            _loadingSince = -1;
        }
        else if (_loadingSince < 0)
        {
            _loadingSince = seconds;
        }

        if (!waiting || seconds - _loadingSince < _loadingDelaySeconds)
        {
            _chromeDirty |= _loadingPanel.Set(false, string.Empty, null, -1);
            return;
        }

        var mft = _explorer.MasterFileTable;
        if (mft != null && mft.RecordCount > 0)
        {
            var title = string.Format(CultureInfo.CurrentCulture, Res.LoadingMasterFileTable, _explorer.MasterFileTableDrive);
            var detail = string.Format(CultureInfo.CurrentCulture, Res.LoadingRecords, mft.RecordsRead, mft.RecordCount);
            _chromeDirty |= _loadingPanel.Set(true, title, detail, (float)mft.RecordsRead / mft.RecordCount);
            return;
        }

        var root = _explorer.Root;
        var tree = _explorer.Tree;
        var name = root >= 0 && root < tree.Count ? tree.GetName(root).ToString() : string.Empty;
        _chromeDirty |= _loadingPanel.Set(true, string.Format(CultureInfo.CurrentCulture, Res.LoadingScanning, name), null, -1);
    }

    private void UpdateTitle()
    {
        var seconds = _statistics.Seconds;
        if (seconds - _lastTitleUpdate < 1)
            return;

        _lastTitleUpdate = seconds;
        Text = string.Format(CultureInfo.CurrentCulture, Res.WindowTitleStatistics, Res.WindowTitle, _statistics.FramesPerSecond, _device.AdapterName, _explorer.Tree.Count, _renderer.VisibleInstances);
    }

    private void ToggleFullScreen()
    {
        // the style stays, the window only covers the monitor with no frame, a window switched to a popup and back loses its composition content.
        // the flag changes before the window does, WM_NCCALCSIZE runs inside these calls and must see the state being entered.
        // the rectangle comes back as it was, a maximized one included, the window placement is not always applied to a window that only moved.
        if (_isFullScreen)
        {
            _isFullScreen = false;
            ApplyFullScreenFrame();
            var rect = _windowedRect;
            SetWindowPos(HWND.Null, rect.left, rect.top, rect.Width, rect.Height, SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
            ApplyMaterial();
            return;
        }

        var monitor = DirectN.Extensions.Utilities.Monitor.FromWindow(Handle, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (monitor == null)
            return;

        _windowedRect = WindowRect;
        _isFullScreen = true;
        ApplyFullScreenFrame();
        var bounds = monitor.Bounds;
        SetWindowPos(HWND.Null, bounds.left, bounds.top, bounds.Width, bounds.Height, SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
        ApplyMaterial();
    }

    // Windows 11 draws a one pixel border and rounds the corners of a window with a frame, which full screen must not show.
    private unsafe void ApplyFullScreenFrame()
    {
        if (!_frameAttributesSupported)
            return;

        var border = _isFullScreen ? DirectN.Constants.DWMWA_COLOR_NONE : DirectN.Constants.DWMWA_COLOR_DEFAULT;
        Functions.DwmSetWindowAttribute(Handle, (uint)DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, (nint)(&border), sizeof(uint));
        var corners = (int)(_isFullScreen ? DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND : DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DEFAULT);
        Functions.DwmSetWindowAttribute(Handle, (uint)DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, (nint)(&corners), sizeof(int));
    }

    protected override bool OnResized(WindowResizedType type, SIZE size)
    {
        // a lost device can no longer resize anything, the window only keeps saying why it stopped.
        if (_ready && !_deviceLost)
        {
            _renderer.Journal.Note(string.Create(CultureInfo.InvariantCulture, $"resized {type} {size.cx}x{size.cy}"));
            _sceneWindow?.ResizeAndMove(0, 0, size.cx, size.cy);
            _swapChain.Resize((uint)size.cx, (uint)size.cy);
            _chrome.Resize((uint)size.cx, (uint)size.cy);
            _chrome.RefreshContent();
            _chromeDirty = true;
        }
        return base.OnResized(type, size);
    }

    protected override LRESULT? WindowProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        // a shell context menu draws its own owner drawn items, and only if these reach it.
        if (msg is MessageDecoder.WM_INITMENUPOPUP or MessageDecoder.WM_MENUSELECT or MessageDecoder.WM_DRAWITEM or MessageDecoder.WM_MEASUREITEM or MessageDecoder.WM_MENUCHAR)
        {
            if (ShellN.Extensions.ShellItem.OnContextMenuWindowMessage(hwnd, msg, wParam, lParam, out var menuResult).IsSuccess)
                return menuResult;
        }

        switch (msg)
        {
            case MessageDecoder.WM_PAINT:
                if (_ready && !IsDisposingOrDisposed)
                {
                    RenderFrame();
                }

                if (!IsAnimating)
                {
                    Functions.ValidateRect(hwnd, 0);
                }
                return new();

            case MessageDecoder.WM_NCCALCSIZE:
                if (wParam.Value != 0)
                {
                    if (_isFullScreen)
                        return new();

                    unsafe
                    {
                        // the top frame is left to the drawn caption, only a maximized window, which hangs over the screen edges, keeps it.
                        ref var rect = ref ((NCCALCSIZE_PARAMS*)lParam.Value)->rgrc[0];
                        var frame = FrameThickness;
                        rect.left += frame;
                        rect.right -= frame;
                        rect.bottom -= frame;
                        if (Functions.IsZoomed(hwnd))
                        {
                            rect.top += frame;
                        }
                    }
                    return new();
                }
                break;

            case MessageDecoder.WM_NCHITTEST:
                return HitTest(hwnd, wParam, lParam);

            case MessageDecoder.WM_NCMOUSEMOVE:
                SetHotWindowButton((int)wParam.Value);
                break;

            case MessageDecoder.WM_NCMOUSELEAVE:
                SetHotWindowButton(0);
                break;

            case MessageDecoder.WM_EXITSIZEMOVE:
                ScheduleSave();
                break;

            case MessageDecoder.WM_NCLBUTTONDOWN:
                CloseMenu();
                switch ((int)wParam.Value)
                {
                    // a window button leaves full screen first, the one in the maximize place only does that, back to the window as it was.
                    case TitleBar.HitMinimize:
                        if (_isFullScreen)
                        {
                            ToggleFullScreen();
                        }
                        Show(SHOW_WINDOW_CMD.SW_MINIMIZE);
                        return new();

                    case TitleBar.HitMaximize:
                        if (_isFullScreen)
                        {
                            ToggleFullScreen();
                            return new();
                        }
                        Show(IsZoomed ? SHOW_WINDOW_CMD.SW_RESTORE : SHOW_WINDOW_CMD.SW_MAXIMIZE);
                        return new();

                    case TitleBar.HitClose:
                        Close();
                        return new();
                }
                break;

            case MessageDecoder.WM_WININICHANGE:
                if (_settings.Appearance == Appearance.System && lParam.Value != 0 && Marshal.PtrToStringUni(lParam.Value) == _colorSetChanged)
                {
                    ApplyTheme();
                }
                break;

            case MessageDecoder.WM_LBUTTONDOWN:
                if (OnChromeMouseDown(lParam))
                    return new();

                if (!_menu.IsOpen && PressIslandTile(_renderer.Island.HitTest(ToPoint(lParam).x, ToPoint(lParam).y)))
                    return new();

                Press(MouseButton.Left, lParam);
                return new();

            case MessageDecoder.WM_LBUTTONDBLCLK:
                // double clicking the caption leaves full screen, the way it would maximize or restore a window.
                if (_isFullScreen && !_menu.IsOpen && _titleBar.HitTest(ToPoint(lParam).x, ToPoint(lParam).y) == TitleBar.HitCaption)
                {
                    ToggleFullScreen();
                    return new();
                }

                if (OnChromeMouseDown(lParam))
                    return new();

                // the first click already opened the place, the second one has nothing more to do.
                if (_renderer.Island.HitTest(ToPoint(lParam).x, ToPoint(lParam).y) >= 0)
                    return new();

                Press(MouseButton.Left, lParam);
                _suppressNextClick = true;
                OnDoubleClick(PickAction.Activate);
                return new();

            case MessageDecoder.WM_RBUTTONDOWN:
                if (IsOverChrome(ToPoint(lParam)))
                    return new();

                Press(MouseButton.Right, lParam);
                return new();

            case MessageDecoder.WM_MBUTTONDOWN:
                Press(MouseButton.Middle, lParam);
                BeginDrag(DragMode.Pan);
                return new();

            case MessageDecoder.WM_LBUTTONUP:
            case MessageDecoder.WM_RBUTTONUP:
            case MessageDecoder.WM_MBUTTONUP:
                if (_menu.OnMouseUp())
                {
                    Functions.ReleaseCapture();
                    _chromeDirty = true;
                    ScheduleSave();
                    return new();
                }

                Release(ToPoint(lParam));
                return new();

            case MessageDecoder.WM_MOUSEMOVE:
                OnMouseMove(ToPoint(lParam));
                return new();

            case MessageDecoder.WM_MOUSELEAVE:
                _titleBar.OnMouseLeave();
                _renderer.Island.ClearHover();
                _chromeDirty = true;
                _trackingLeave = false;
                RequestPick(-1, -1, PickAction.Hover);
                return new();

            case MessageDecoder.WM_MOUSEWHEEL:
                if (_menu.IsOpen)
                {
                    var wheelPoint = ToPoint(lParam);
                    Functions.ScreenToClient(hwnd, ref wheelPoint);
                    _menu.OnWheel(wheelPoint.x, wheelPoint.y, (short)(wParam.Value >> 16));
                    _chromeDirty = true;
                    Invalidate(null, false);
                    return new();
                }

                var wheelAt = ToPoint(lParam);
                Functions.ScreenToClient(hwnd, ref wheelAt);
                if (_renderer.Island.Contains(wheelAt.x, wheelAt.y))
                {
                    _renderer.Island.Scroll(-(short)(wParam.Value >> 16) / (float)_wheelDelta * _islandScrollStep * DpiScale);
                    _chromeDirty = true;
                    Invalidate(null, false);
                    return new();
                }

                TakeCamera();
                _camera.Zoom((short)(wParam.Value >> 16) / (float)_wheelDelta);
                Invalidate(null, false);
                return new();

            case MessageDecoder.WM_DPICHANGED:
                unsafe
                {
                    var suggested = *(RECT*)lParam.Value;
                    SetWindowPos(HWND.Null, suggested.left, suggested.top, suggested.Width, suggested.Height, SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
                }
                CloseMenu();
                ScheduleSave();
                _chromeDirty = true;
                Invalidate(null, false);
                return new();

            case MessageDecoder.WM_SYSKEYDOWN:
                switch ((VIRTUAL_KEY)wParam.Value)
                {
                    case VIRTUAL_KEY.VK_RETURN:
                        ToggleFullScreen();
                        return new();

                    case VIRTUAL_KEY.VK_LEFT:
                        OnCaptionButton(CaptionButton.Back);
                        return new();

                    case VIRTUAL_KEY.VK_RIGHT:
                        OnCaptionButton(CaptionButton.Forward);
                        return new();
                }
                break;

            // the mouse's own back and forward buttons.
            case MessageDecoder.WM_XBUTTONUP:
                OnCaptionButton((wParam.Value >> 16 & 0xFFFF) == _xButtonForward ? CaptionButton.Forward : CaptionButton.Back);
                return new() { Value = 1 };

            case MessageDecoder.WM_ACTIVATE:
                if ((wParam.Value & 0xFFFF) == 0)
                {
                    CloseMenu();
                }
                break;

            case MessageDecoder.WM_KEYDOWN:
                if (_menu.IsOpen)
                {
                    _menu.OnKeyDown((VIRTUAL_KEY)wParam.Value);
                    _chromeDirty = true;
                    Invalidate(null, false);
                    return new();
                }

                if ((VIRTUAL_KEY)wParam.Value == VIRTUAL_KEY.VK_ESCAPE)
                {
                    if (_isFullScreen)
                    {
                        ToggleFullScreen();
                    }
                    else
                    {
                        _navigator.ClearSelection();
                    }
                    Invalidate(null, false);
                    return new();
                }

                if ((VIRTUAL_KEY)wParam.Value == VIRTUAL_KEY.VK_BACK)
                {
                    _cameraMovedByUser = true;
                    _navigator.GoToParent();
                    Invalidate(null, false);
                    return new();
                }

                if ((VIRTUAL_KEY)wParam.Value == VIRTUAL_KEY.VK_RETURN && _navigator.Selected >= 0)
                {
                    _navigator.Activate(_navigator.Selected);
                    Invalidate(null, false);
                    return new();
                }

                if ((VIRTUAL_KEY)wParam.Value == VIRTUAL_KEY.VK_HOME)
                {
                    _navigator.ClearSelection();
                    FrameAll();
                    return new();
                }

                if ((VIRTUAL_KEY)wParam.Value == VIRTUAL_KEY.VK_C)
                {
                    ToggleColorMode();
                    return new();
                }

                if ((VIRTUAL_KEY)wParam.Value == VIRTUAL_KEY.VK_E)
                {
                    _settings.Effect = (ScreenEffect)(((int)_settings.Effect + 1) % Enum.GetValues<ScreenEffect>().Length);
                    ApplySettings();
                    OnSettingChanged();
                    return new();
                }

                if ((VIRTUAL_KEY)wParam.Value == VIRTUAL_KEY.VK_H)
                {
                    ToggleHidden();
                    return new();
                }

                if ((VIRTUAL_KEY)wParam.Value == VIRTUAL_KEY.VK_L)
                {
                    _settings.ShowLegend = !_settings.ShowLegend;
                    ApplySettings();
                    OnSettingChanged();
                    return new();
                }

                if ((VIRTUAL_KEY)wParam.Value == VIRTUAL_KEY.VK_F1)
                {
                    var show = !_settings.ShowStatus;
                    _settings.ShowStatus = show;
                    _settings.ShowPerformance = show;
                    _settings.ShowLegend = show;
                    ApplySettings();
                    OnSettingChanged();
                    return new();
                }

                if ((VIRTUAL_KEY)wParam.Value == VIRTUAL_KEY.VK_F2)
                {
                    RestartAsAdministrator();
                    return new();
                }

                if ((VIRTUAL_KEY)wParam.Value == VIRTUAL_KEY.VK_F3)
                {
                    _settings.ShowPerformance = !_settings.ShowPerformance;
                    ApplySettings();
                    OnSettingChanged();
                    return new();
                }
                break;
        }
        return base.WindowProc(hwnd, msg, wParam, lParam);
    }

    // the master file table can only be read elevated. the settings are saved first,
    // so the elevated process opens the same place with the same camera, and this one goes away.
    private void RestartAsAdministrator()
    {
        var path = Environment.ProcessPath;
        if (path == null || Environment.IsPrivilegedProcess)
            return;

        SaveSettingsNow();
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = _runAsVerb });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == (int)WIN32_ERROR.ERROR_CANCELLED)
        {
            return;
        }
        Close();
    }

    private bool IsOverChrome(POINT point) => _menu.IsOpen || _titleBar.Contains(point.x, point.y);

    private bool OnChromeMouseDown(LPARAM lParam)
    {
        var point = ToPoint(lParam);
        if (!IsOverChrome(point))
            return false;

        if (_menu.IsOpen)
        {
            _menu.OnMouseDown(point.x, point.y);
            if (_menu.IsCapturing)
            {
                Functions.SetCapture(Handle);
            }
        }
        else
        {
            _titleBar.OnMouseDown(point.x, point.y);
        }
        _chromeDirty = true;
        Invalidate(null, false);
        return true;
    }

    private void CloseMenu()
    {
        if (!_menu.IsOpen)
            return;

        _menu.Close();
        _chromeDirty = true;
        Invalidate(null, false);
    }

    private void ToggleHidden()
    {
        _settings.ShowHidden = !_settings.ShowHidden;
        ApplySettings();
        OnSettingChanged();
    }

    private void ToggleColorMode()
    {
        _settings.ColorMode = _layout.ColorMode == ColorMode.Type ? ColorMode.Age : ColorMode.Type;
        ApplySettings();
        OnSettingChanged();
    }

    private void Press(MouseButton button, LPARAM lParam)
    {
        _pressedButton = button;
        _pressPoint = ToPoint(lParam);
        _lastMouse = _pressPoint;
        Functions.SetCapture(Handle);
    }

    // a press becomes a drag only once the cursor has left the system drag rectangle, short of that it is a click.
    private unsafe void OnMouseMove(POINT point)
    {
        var deltaX = point.x - _lastMouse.x;
        var deltaY = point.y - _lastMouse.y;
        _lastMouse = point;
        if (!_trackingLeave)
        {
            var track = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE, hwndTrack = Handle };
            _trackingLeave = Functions.TrackMouseEvent(ref track);
        }

        if (_pressedButton == MouseButton.None && _dragMode == DragMode.None)
        {
            _chromeDirty |= _menu.IsOpen ? _menu.OnMouseMove(point.x, point.y) : _titleBar.OnMouseMove(point.x, point.y);
            if (IsOverChrome(point))
            {
                _renderer.Island.ClearHover();
                RequestPick(-1, -1, PickAction.Hover);
                return;
            }

            _chromeDirty |= _renderer.Island.SetHover(point.x, point.y);
            if (_renderer.Island.HoveredTile >= 0)
            {
                RequestPick(-1, -1, PickAction.Hover);
                Invalidate(null, false);
                return;
            }
        }

        if (_pressedButton != MouseButton.None && _dragMode == DragMode.None)
        {
            var dragWidth = Functions.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDRAG);
            var dragHeight = Functions.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYDRAG);
            if (Math.Abs(point.x - _pressPoint.x) > dragWidth || Math.Abs(point.y - _pressPoint.y) > dragHeight)
            {
                BeginDrag(_pressedButton == MouseButton.Left ? DragMode.Orbit : DragMode.Pan);
            }
        }

        switch (_dragMode)
        {
            case DragMode.Orbit:
                _camera.Orbit(deltaX, deltaY);
                break;

            case DragMode.Pan:
                _camera.Pan(deltaX, deltaY, _swapChain.Height);
                break;

            default:
                RequestPick(point.x, point.y, PickAction.Hover);
                break;
        }
        Invalidate(null, false);
    }

    private void Release(POINT point)
    {
        var button = _pressedButton;
        var wasClick = _dragMode == DragMode.None && !_suppressNextClick;
        _pressedButton = MouseButton.None;
        _dragMode = DragMode.None;
        _suppressNextClick = false;
        Functions.ReleaseCapture();
        if (wasClick)
        {
            switch (button)
            {
                case MouseButton.Left:
                    _lastClick = new PickRequest(point.x, point.y, PickAction.Select);
                    RequestPick(point.x, point.y, PickAction.Select);
                    break;

                case MouseButton.Right:
                    RequestPick(point.x, point.y, PickAction.ContextMenu);
                    break;
            }
        }
        Invalidate(null, false);
    }

    // the first click already knows what was under the cursor, and the scene has not moved since, so that is what the double click acts on.
    private void OnDoubleClick(PickAction action)
    {
        var click = _navigator.GetLastPick(PickAction.Select);
        if (click != null && _lastClick != null && click.Value.Request == _lastClick.Value && click.Value.Entry >= 0)
        {
            _cameraMovedByUser = true;
            _navigator.HandlePick(new PickResult(new PickRequest(_pressPoint.x, _pressPoint.y, action), click.Value.Entry), _statistics.Seconds);
            Invalidate(null, false);
            return;
        }
        RequestPick(_pressPoint.x, _pressPoint.y, action);
    }

    private void BeginDrag(DragMode mode)
    {
        _dragMode = mode;
        TakeCamera();
        Invalidate(null, false);
    }

    // the user moving the camera ends any flight and stops the camera from following the scan or the selection.
    private void TakeCamera()
    {
        _cameraMovedByUser = true;
        _camera.CancelFlight();
        _navigator.StopFollowing();
    }

    private static POINT ToPoint(LPARAM lParam) => new() { x = (short)(lParam.Value & 0xFFFF), y = (short)((lParam.Value >> 16) & 0xFFFF) };

    protected override void Dispose(bool disposing)
    {
        if (disposing && _ready)
        {
            _ready = false;
            _saveTimer.Dispose();
            CapturePosition();
            _settingsFile.Save(_settings);
            _explorer.Dispose();
            _thumbnails.Dispose();
            _places.Dispose();
            _device.WaitForIdle();
            _pendingCapture?.Dispose();
            _chromeResources?.Dispose();
            _performancePanel.Dispose();
            _tooltipPanel.Dispose();
            _placeImages.Dispose();
            _loadingPanel.Dispose();
            _captionTooltip.Dispose();
            _chrome.Dispose();
            _renderer.Dispose();
            _commandList.Dispose();
            _swapChain.Dispose();
            _sceneWindow?.Dispose();
            _heaps.Dispose();
            _device.Dispose();
        }
        base.Dispose(disposing);
    }

    private enum DragMode
    {
        None,
        Orbit,
        Pan,
    }

    private enum MouseButton
    {
        None,
        Left,
        Right,
        Middle,
    }
}
