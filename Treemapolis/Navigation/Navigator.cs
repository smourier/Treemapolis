namespace Treemapolis.Navigation;

// what the user points at and what they chose, and what choosing does: gliding there, diving into a folder, launching a file.
public sealed class Navigator(NamespaceExplorer explorer, LayoutEngine layout, SceneRenderer renderer, Camera camera, ShellCommands shell)
{
    private const float _viewScale = 1.3f;
    private const float _minimumViewDistance = 2;
    private const float _refollowDistance = 0.5f;
    private int _following = Entry.None;
    private Vector3 _followTarget;
    private int _deferredFlight = Entry.None;
    private double _deferredFlightTime;
    private readonly PickResult?[] _lastPicks = new PickResult?[Enum.GetValues<PickAction>().Length];
    private readonly NavigationHistory _history = new();
    private readonly HashSet<int> _pendingExpanded = [];
    private NavigationLocation? _pendingLocation;
    private string? _pendingSelection;

    public int Hovered { get; private set; } = Entry.None;
    public int Selected { get; private set; } = Entry.None;
    public Vector2 HoverPoint { get; private set; }

    public PickResult? GetLastPick(PickAction action) => _lastPicks[(int)action];
    public bool HasDeferredFlight => _deferredFlight != Entry.None;
    public bool HasPendingLocation => _pendingLocation != null || _pendingSelection != null;
    public NavigationHistory History => _history;

    // what reveal in Explorer shows, the selection or else the folder the map is laid out from.
    public int RevealTarget => Selected >= 0 ? Selected : layout.MapRoot;

    public NavigationLocation? CurrentLocation
    {
        get
        {
            var tree = explorer.Tree;
            var root = explorer.Root;
            if (root < 0 || root >= tree.Count)
                return _pendingLocation;

            var path = new List<string>();
            var mapRoot = layout.MapRoot;
            for (var current = mapRoot >= 0 && mapRoot < tree.Count ? mapRoot : root; current != Entry.None && current != root; current = tree[current].Parent)
            {
                path.Add(tree.GetName(current).ToString());
            }
            path.Reverse();

            var name = tree.GetName(root).ToString();
            var node = tree.GetShellNode(root);
            return new NavigationLocation { IdList = node?.IdList, ParsingName = node?.ParsingName ?? name, DisplayName = name, Path = path };
        }
    }

    public void HandlePick(PickResult result, double time)
    {
        _lastPicks[(int)result.Request.Action] = result;
        switch (result.Request.Action)
        {
            case PickAction.Hover:
                Hovered = result.Entry;
                HoverPoint = new Vector2(result.Request.X, result.Request.Y);
                break;

            case PickAction.Select:
                SelectFromClick(result.Entry, time);
                break;

            case PickAction.Activate:
                Activate(result.Entry);
                break;
        }
        Sync();
    }

    public void Select(int entry, bool fly = true)
    {
        var snapshot = renderer.Layout;
        if (snapshot == null || !snapshot.IsDisplayed(entry))
        {
            Selected = Entry.None;
            _following = Entry.None;
            Sync();
            return;
        }

        Selected = entry;
        if (fly)
        {
            _following = entry;
            FlyTo(snapshot, entry);
        }
        Sync();
    }

    // a click may be the first half of a double click, the camera waits for the double click time before it moves,
    // otherwise the second click would land on whatever the flight brought under the cursor.
    public void SelectFromClick(int entry, double time)
    {
        Select(entry, false);
        if (Selected == Entry.None)
            return;

        _deferredFlight = Selected;
        _deferredFlightTime = time + Functions.GetDoubleClickTime() / 1000.0;
    }

    public void Update(double time)
    {
        ResolvePendingLocation();
        ResolvePendingSelection();
        if (_deferredFlight == Entry.None || time < _deferredFlightTime)
            return;

        var entry = _deferredFlight;
        _deferredFlight = Entry.None;
        if (entry == Selected)
        {
            Select(entry);
        }
    }

    public void ClearSelection()
    {
        Selected = Entry.None;
        _following = Entry.None;
        _deferredFlight = Entry.None;
        Sync();
    }

    // a folder becomes the whole map, a block of small items dives into the folder that holds them, a file is launched the way Explorer would.
    public void Activate(int entry)
    {
        var tree = explorer.Tree;
        if (entry < 0 || entry >= tree.Count)
            return;

        _deferredFlight = Entry.None;
        var snapshot = renderer.Layout;
        if (snapshot != null && snapshot.AggregateInfos.TryGetValue(entry, out var aggregate))
        {
            Dive(aggregate.Container);
            return;
        }

        if (!tree[entry].IsContainer)
        {
            Select(entry, false);
            shell.Open(tree, entry);
            return;
        }
        Dive(entry);
    }

    public void Dive(int entry)
    {
        if (entry == layout.MapRoot || !CanDive(entry))
            return;

        Remember();
        DiveCore(entry);
    }

    public void OpenLocation(byte[] idList)
    {
        ArgumentNullException.ThrowIfNull(idList);
        Remember();
        ClearPending();
        ResetPointing();
        explorer.Open(idList);
    }

    public void OpenLocation(string parsingName)
    {
        ArgumentNullException.ThrowIfNull(parsingName);
        Remember();
        ClearPending();
        ResetPointing();
        explorer.Open(parsingName);
    }

    public void GoBack()
    {
        var current = CurrentLocation;
        if (current != null)
        {
            Restore(_history.Back(current));
        }
    }

    public void GoForward()
    {
        var current = CurrentLocation;
        if (current != null)
        {
            Restore(_history.Forward(current));
        }
    }

    public bool CanGoBack => _history.BackCount > 0 && CurrentLocation != null;
    public bool CanGoForward => _history.ForwardCount > 0 && CurrentLocation != null;

    private bool CanDive(int entry)
    {
        var tree = explorer.Tree;
        return entry >= 0 && entry < tree.Count && tree[entry].IsContainer;
    }

    private void Remember()
    {
        var current = CurrentLocation;
        if (current != null)
        {
            _history.Push(current);
        }
    }

    // a location in the tree already open is a dive, anywhere else is opened again and dived into as the scan brings the path back.
    // a location given on the command line, the item to select is found once the scan has listed it.
    public void StartAt(StartLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        ClearPending();
        ResetPointing();
        _pendingSelection = location.SelectName;
        explorer.Open(location.IdList);
    }

    // where the last session was, opened without a step in the history.
    public void StartAt(NavigationLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        Restore(location);
    }

    private void Restore(NavigationLocation? target)
    {
        if (target == null)
            return;

        var current = CurrentLocation;
        ClearPending();
        _pendingLocation = target;
        if (current == null || explorer.Root < 0 || !target.IsSameRoot(current))
        {
            ResetPointing();
            if (target.IdList != null)
            {
                explorer.Open(target.IdList);
            }
            else
            {
                explorer.Open(target.ParsingName);
            }
        }
        ResolvePendingLocation();
    }

    private void ResolvePendingLocation()
    {
        var target = _pendingLocation;
        if (target == null)
            return;

        var tree = explorer.Tree;
        var entry = explorer.Root;
        if (entry < 0 || entry >= tree.Count)
        {
            if (!explorer.IsBusy)
            {
                ClearPending();
            }
            return;
        }

        foreach (var name in target.Path)
        {
            var child = FindChild(tree, entry, name);
            if (child == Entry.None)
            {
                // still scanning, the name may arrive later, otherwise the map stops at the deepest folder that is still there.
                if (!explorer.IsBusy)
                {
                    DiveCore(entry);
                    ClearPending();
                }
                return;
            }

            // a shell folder is enumerated once, a second enumeration running beside the first would add its items twice.
            if (_pendingExpanded.Add(child))
            {
                explorer.Expand(child);
            }
            entry = child;
        }

        DiveCore(entry);
        ClearPending();
    }

    private void ResolvePendingSelection()
    {
        var name = _pendingSelection;
        var snapshot = renderer.Layout;
        var tree = explorer.Tree;
        var root = explorer.Root;
        if (name == null || root < 0 || root >= tree.Count)
            return;

        var child = tree.FindChild(root, name);
        if (child != Entry.None)
        {
            // selected once the map shows it, the camera then flies to it.
            if (snapshot == null || snapshot.Tree != tree || !snapshot.IsDisplayed(child))
                return;

            _pendingSelection = null;
            Select(child);
            return;
        }

        if (!explorer.IsBusy)
        {
            _pendingSelection = null;
        }
    }

    private static int FindChild(NamespaceTree tree, int parent, string name)
    {
        var child = tree.FindChild(parent, name);
        return child != Entry.None && tree[child].IsContainer ? child : Entry.None;
    }

    private void ClearPending()
    {
        _pendingLocation = null;
        _pendingSelection = null;
        _pendingExpanded.Clear();
    }

    private void ResetPointing()
    {
        Selected = Entry.None;
        Hovered = Entry.None;
        _following = Entry.None;
        _deferredFlight = Entry.None;
        Sync();
    }

    // what is below a folder that was only listed gets scanned on the way in, the map fills in as it arrives.
    private void DiveCore(int entry)
    {
        if (!CanDive(entry))
            return;

        explorer.Expand(entry);
        Selected = Entry.None;
        Hovered = Entry.None;
        _following = Entry.None;
        _deferredFlight = Entry.None;
        layout.SetMapRoot(entry);
        Sync();
    }

    // up one level: the selection first, then the map root, and from the scanned root up through the shell, This PC being above a drive.
    public void GoToParent()
    {
        var snapshot = renderer.Layout;
        if (snapshot != null && Selected >= 0 && Selected != snapshot.Root && snapshot.IsDisplayed(Selected))
        {
            Select(snapshot.Instances[Selected].Parent);
            return;
        }

        var tree = explorer.Tree;
        var mapRoot = layout.MapRoot;
        if (mapRoot >= 0 && mapRoot != explorer.Root)
        {
            Remember();
            DiveCore(tree[mapRoot].Parent);
            Selected = mapRoot;
            Sync();
            return;
        }

        var root = explorer.Root;
        if (root < 0)
            return;

        var parent = ShellCommands.GetParentIdList(explorer.Tree, root);
        if (parent != null)
        {
            Remember();
            ClearPending();
            ResetPointing();
            explorer.Open(parent);
        }
    }

    public string? ShowContextMenu(int entry)
    {
        var tree = explorer.Tree;
        var target = entry >= 0 ? entry : explorer.Root;
        if (target < 0)
            return null;

        if (entry >= 0)
        {
            Select(entry, false);
        }
        return shell.ShowContextMenu(tree, target);
    }

    public void Reveal()
    {
        var target = RevealTarget;
        if (target >= 0)
        {
            ShellCommands.Reveal(explorer.Tree, target);
        }
    }

    public void StopFollowing() => _following = Entry.None;

    // an entry moves when the layout changes around it, the camera keeps going to where it is now.
    public void OnLayoutChanged(LayoutSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (Selected >= 0 && !snapshot.IsDisplayed(Selected) && snapshot.Tree == explorer.Tree)
        {
            ClearSelection();
        }

        if (_following >= 0 && snapshot.IsDisplayed(_following))
        {
            FlyTo(snapshot, _following, true);
        }
    }

    public string? DescribeHovered()
    {
        var tree = explorer.Tree;
        var entry = Hovered;
        if (entry < 0 || entry >= tree.Count)
            return null;

        var snapshot = renderer.Layout;
        if (snapshot != null && snapshot.AggregateInfos.TryGetValue(entry, out var aggregate))
            return string.Format(CultureInfo.CurrentCulture, Res.TooltipAggregate, tree.GetName(aggregate.Container).ToString(), aggregate.Count, FormatBytes(aggregate.Size));

        ref readonly var item = ref tree[entry];
        var name = tree.GetName(entry).ToString();
        var node = tree.GetShellNode(entry);
        if (node != null && node.DriveCapacity > 0)
            return string.Format(CultureInfo.CurrentCulture, Res.TooltipDrive, name, FormatBytes(node.DriveCapacity - node.DriveFreeSpace), FormatBytes(node.DriveCapacity));

        if (item.IsContainer)
            return string.Format(CultureInfo.CurrentCulture, Res.TooltipContainer, name, item.DescendantCount, FormatBytes(item.TotalSize));

        if (item.LastWriteMinutes <= 0)
            return string.Format(CultureInfo.CurrentCulture, Res.TooltipFileWithoutDate, name, FormatBytes(item.Size));

        return string.Format(CultureInfo.CurrentCulture, Res.TooltipFile, name, FormatBytes(item.Size), item.LastWriteTimeUtc.ToLocalTime());
    }

    public static unsafe string FormatBytes(long bytes)
    {
        const int bufferLength = 64;
        var buffer = stackalloc char[bufferLength];
        Functions.StrFormatByteSizeW(bytes, new PWSTR { Value = (nint)buffer }, bufferLength);
        return new string(buffer);
    }

    private void FlyTo(LayoutSnapshot snapshot, int entry, bool onlyIfMoved = false)
    {
        ref readonly var instance = ref snapshot.Instances[entry];
        var target = instance.Position + new Vector3(0, instance.Size.Y, 0);

        // a scan streaming in relays out several times a second, restarting a flight that barely moved would only make it stutter.
        if (onlyIfMoved && Vector3.Distance(target, _followTarget) < _refollowDistance)
            return;

        _followTarget = target;
        var distance = MathF.Max(MathF.Max(instance.Size.X, instance.Size.Z) * _viewScale, _minimumViewDistance);
        camera.FlyTo(target, distance);
    }

    private void Sync()
    {
        renderer.HoveredEntry = Hovered;
        renderer.SelectedEntry = Selected;
    }
}
