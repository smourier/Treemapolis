namespace Treemapolis.Layout;

// rebuilds the layout off the UI thread whenever the tree, the map root or a setting changes, never more often than a layout costs,
// so a scan streaming thousands of entries a second reshapes the map a few times a second rather than stalling on it.
public sealed class LayoutEngine
{
    private const double _minimumIntervalSeconds = 0.25;
    private const double _costMultiplier = 3;

    private readonly NamespaceExplorer _explorer;
    private readonly TreemapLayout _layout = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Task? _running;
    private LayoutSnapshot? _latest;
    private NamespaceTree? _builtTree;
    private int _builtRoot = Entry.None;
    private int _builtTreeVersion = -1;
    private int _builtSettingsVersion = -1;
    private long _builtTimestamp;
    private int _settingsVersion;
    private LayoutOptions _options = new(ColorMode.Type, false, 1);
    private NamespaceTree? _focusTree;
    private int _focus = Entry.None;
    private double _lastStartSeconds = double.MinValue;
    private double _lastCostSeconds;

    public LayoutEngine(NamespaceExplorer explorer)
    {
        ArgumentNullException.ThrowIfNull(explorer);
        _explorer = explorer;
    }

    public LayoutSnapshot? Latest => Volatile.Read(ref _latest);

    // the Stopwatch timestamp the frame clock counts from, changes are stamped against it for the shaders.
    public long ClockEpoch { get; set; }
    public Exception? LastError { get; private set; }

    public ColorMode ColorMode => _options.ColorMode;

    public LayoutOptions Options
    {
        get => _options;
        set
        {
            if (_options == value)
                return;

            _options = value;
            Invalidate();
        }
    }

    // the container the map is laid out from, the scanned root unless the user dived into something below it.
    public int MapRoot => _focusTree == _explorer.Tree && _focus != Entry.None ? _focus : _explorer.Root;

    public void SetMapRoot(int entry)
    {
        var tree = _explorer.Tree;
        _focusTree = tree;
        _focus = entry == _explorer.Root ? Entry.None : entry;
        Invalidate();
    }

    public bool IsBusy => (_running != null && !_running.IsCompleted) || IsStale;

    private bool IsStale
    {
        get
        {
            var tree = _explorer.Tree;
            var root = MapRoot;
            if (root == Entry.None)
                return false;

            // what was removed stays on the map while it sinks, it goes once that is over.
            var removalsEnd = tree.RemovalsEndTimestamp;
            var removalsOver = removalsEnd > _builtTimestamp && Stopwatch.GetTimestamp() >= removalsEnd;
            return tree != _builtTree || root != _builtRoot || tree.Version != _builtTreeVersion || _settingsVersion != _builtSettingsVersion || removalsOver;
        }
    }

    // a change the user asked for is laid out at once, only the flow of a scan is throttled.
    private void Invalidate()
    {
        Interlocked.Increment(ref _settingsVersion);
        _lastStartSeconds = double.MinValue;
    }

    public void Update()
    {
        if ((_running != null && !_running.IsCompleted) || !IsStale)
            return;

        var now = _clock.Elapsed.TotalSeconds;
        if (now - _lastStartSeconds < Math.Max(_minimumIntervalSeconds, _lastCostSeconds * _costMultiplier))
            return;

        var tree = _explorer.Tree;
        var root = MapRoot;
        var options = _options;
        var settingsVersion = _settingsVersion;
        _builtTree = tree;
        _builtRoot = root;
        _builtTreeVersion = tree.Version;
        _builtSettingsVersion = settingsVersion;
        _builtTimestamp = Stopwatch.GetTimestamp();
        var clockEpoch = ClockEpoch;
        _lastStartSeconds = now;
        _running = Task.Run(() =>
        {
            try
            {
                var snapshot = TreemapLayout.Build(tree, root, options, settingsVersion, clockEpoch, CancellationToken.None);
                _lastCostSeconds = snapshot.BuildMilliseconds / 1000;
                Volatile.Write(ref _latest, snapshot);
            }
            catch (Exception ex)
            {
                Application.TraceError(ex.ToString());
                LastError = ex;
            }
        });
    }
}
