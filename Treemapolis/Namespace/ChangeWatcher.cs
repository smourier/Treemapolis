using ShellN;
using ShellN.Extensions;

namespace Treemapolis.Namespace;

// keeps a scanned tree current: the file system reports what changed under the folders that were scanned whole,
// and the shell reports drives that come and go. changes arrive in bursts, a copy fires thousands a second,
// so paths are only collected as they come and applied together a moment later, each path checked once against what the disk says now.
public sealed class ChangeWatcher : IDisposable
{
    private const int _flushMilliseconds = 150;
    private const int _bufferSize = 64 * 1024;
    private static readonly TimeSpan _recentChange = TimeSpan.FromMinutes(2);
    private const char _separator = '\\';
    private const SHCNE_ID _driveEvents = SHCNE_ID.SHCNE_DRIVEADD | SHCNE_ID.SHCNE_DRIVEREMOVED | SHCNE_ID.SHCNE_DRIVEADDGUI | SHCNE_ID.SHCNE_MEDIAINSERTED | SHCNE_ID.SHCNE_MEDIAREMOVED;
    private const SHCNE_ID _driveAddEvents = SHCNE_ID.SHCNE_DRIVEADD | SHCNE_ID.SHCNE_DRIVEADDGUI | SHCNE_ID.SHCNE_MEDIAINSERTED;

    private readonly NamespaceTree _tree;
    private readonly Action<int, string> _scanFolder;
    private readonly Lock _lock = new();
    private readonly List<WatchedFolder> _folders = [];
    private readonly ConcurrentQueue<string> _paths = new();
    private readonly EntryBatch _batch = new();
    private readonly Timer _timer;
    private ChangeNotifier? _drives;
    private int _computer = Entry.None;
    private int _armed;
    private int _applied;
    private int _lost;
    private bool _disposed;

    // a folder added while watching is scanned whole by the explorer, what it holds arrives the way a scan does.
    public ChangeWatcher(NamespaceTree tree, Action<int, string> scanFolder)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(scanFolder);
        _tree = tree;
        _scanFolder = scanFolder;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsEnabled { get; set; } = true;
    public int Applied => Volatile.Read(ref _applied);

    // a folder below one already watched is covered by it, the watch there includes subfolders.
    public void WatchFolder(int entry, string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        // kept without its last separator, which a drive root keeps through Path.TrimEndingDirectorySeparator, so C:\ becomes C: here.
        var fullPath = Path.GetFullPath(path);
        var root = fullPath.TrimEnd(_separator);
        lock (_lock)
        {
            if (_disposed || _folders.Exists(folder => IsBelow(root, folder.Path)))
                return;

            try
            {
                var watcher = new FileSystemWatcher(fullPath)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    IncludeSubdirectories = true,
                    InternalBufferSize = _bufferSize,
                };
                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Changed += OnChanged;
                watcher.Renamed += OnRenamed;
                watcher.Error += OnError;
                watcher.EnableRaisingEvents = true;
                _folders.Add(new WatchedFolder(entry, root, watcher));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                Application.TraceWarning($"'{root}' cannot be watched: {ex.Message}");
            }
        }
    }

    // the drives are the children of This PC, wherever This PC sits in the tree.
    public void WatchDrives(int computer)
    {
        lock (_lock)
        {
            if (_disposed || _drives != null)
                return;

            _computer = computer;
            _drives = new ChangeNotifier();
            _drives.Notified += OnDriveNotified;
            _ = _drives.Run(null, true, _driveEvents);
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Enqueue(e.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        Enqueue(e.OldFullPath);
        Enqueue(e.FullPath);
    }

    // too many changes at once and Windows drops them, a folder deleted with thousands of files is enough.
    // nothing then says what changed, so every folder whose date moved is read again and compared.
    private void OnError(object sender, ErrorEventArgs e)
    {
        Application.TraceWarning($"changes were lost: {e.GetException().Message}");
        Interlocked.Exchange(ref _lost, 1);
        if (Interlocked.Exchange(ref _armed, 1) == 0)
        {
            _timer.Change(_flushMilliseconds, Timeout.Infinite);
        }
    }

    private void Enqueue(string path)
    {
        if (!IsEnabled)
            return;

        _paths.Enqueue(path);
        if (Interlocked.Exchange(ref _armed, 1) == 0)
        {
            _timer.Change(_flushMilliseconds, Timeout.Infinite);
        }
    }

    private void Flush()
    {
        Interlocked.Exchange(ref _armed, 0);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (_paths.TryDequeue(out var path))
        {
            paths.Add(path);
        }

        lock (_lock)
        {
            if (_disposed)
                return;

            if (Interlocked.Exchange(ref _lost, 0) != 0)
            {
                foreach (var folder in _folders)
                {
                    Resynchronize(folder);
                }
                return;
            }

            foreach (var path in paths)
            {
                try
                {
                    Apply(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Application.TraceVerbose($"'{path}' could not be read after it changed: {ex.Message}");
                }
            }
        }
    }

    // what the disk holds now is what counts, whatever sequence of events led there.
    private void Apply(string path)
    {
        if (!TryResolve(path, out var parent, out var name))
            return;

        var existing = _tree.FindChild(parent, name);
        var parentListed = (_tree[parent].Flags & EntryFlags.Enumerated) != 0;
        if (Directory.Exists(path))
        {
            // a folder that changed only says something inside it did, and that arrives on its own.
            if (existing != Entry.None || !parentListed)
                return;

            var directory = new DirectoryInfo(path);
            var index = Append(parent, name, 0, directory.LastWriteTimeUtc, directory.Attributes);
            _scanFolder(index, path);
            return;
        }

        if (File.Exists(path))
        {
            var file = new FileInfo(path);
            if (existing == Entry.None)
            {
                if (parentListed)
                {
                    Append(parent, name, file.Length, file.LastWriteTimeUtc, file.Attributes);
                }
                return;
            }

            ref readonly var entry = ref _tree[existing];
            if (entry.IsContainer || (entry.Size == file.Length && entry.LastWriteMinutes == Entry.ToMinutes(file.LastWriteTimeUtc)))
                return;

            _tree.Update(existing, file.Length, file.LastWriteTimeUtc);
            Interlocked.Increment(ref _applied);
            return;
        }

        if (existing != Entry.None)
        {
            _tree.Remove(existing);
            Interlocked.Increment(ref _applied);
        }
    }

    // folders walked from the watched one down, only those whose date moved since they were read have their content compared,
    // a folder's date moves whenever something is added, removed or renamed right inside it.
    private void Resynchronize(WatchedFolder folder)
    {
        var queue = new Queue<(int Entry, string Path)>();
        queue.Enqueue((folder.Entry, folder.Path));
        while (queue.TryDequeue(out var item))
        {
            ref readonly var entry = ref _tree[item.Entry];
            if ((entry.Flags & (EntryFlags.Enumerated | EntryFlags.Removed | EntryFlags.ReparsePoint)) != EntryFlags.Enumerated)
                continue;

            var directory = new DirectoryInfo(item.Path + _separator);
            try
            {
                if (!directory.Exists)
                {
                    _tree.Remove(item.Entry);
                    Interlocked.Increment(ref _applied);
                    continue;
                }

                var lastWrite = directory.LastWriteTimeUtc;
                // the tree keeps dates to the minute, a folder changed within the last ones is compared whatever its date says.
                if (Entry.ToMinutes(lastWrite) != entry.LastWriteMinutes || DateTime.UtcNow - lastWrite < _recentChange || item.Entry == folder.Entry)
                {
                    Compare(item.Entry, item.Path, directory);
                    _tree.SetLastWrite(item.Entry, lastWrite);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            for (var child = _tree[item.Entry].FirstChild; child != Entry.None; child = _tree[child].NextSibling)
            {
                if (_tree[child].IsContainer && (_tree[child].Flags & EntryFlags.Removed) == 0)
                {
                    queue.Enqueue((child, item.Path + _separator + _tree.GetName(child).ToString()));
                }
            }
        }
    }

    // what a folder holds on disk against what the tree has for it: gone, changed and new.
    private void Compare(int index, string path, DirectoryInfo directory)
    {
        var onDisk = new Dictionary<string, FileSystemInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in directory.EnumerateFileSystemInfos())
        {
            onDisk[info.Name] = info;
        }

        for (var child = _tree[index].FirstChild; child != Entry.None; child = _tree[child].NextSibling)
        {
            ref readonly var entry = ref _tree[child];
            if ((entry.Flags & EntryFlags.Removed) != 0)
                continue;

            if (!onDisk.Remove(_tree.GetName(child).ToString(), out var info))
            {
                _tree.Remove(child);
                Interlocked.Increment(ref _applied);
                continue;
            }

            if (info is FileInfo file && !entry.IsContainer && (entry.Size != file.Length || entry.LastWriteMinutes != Entry.ToMinutes(file.LastWriteTimeUtc)))
            {
                _tree.Update(child, file.Length, file.LastWriteTimeUtc);
                Interlocked.Increment(ref _applied);
            }
        }

        foreach (var info in onDisk.Values)
        {
            var added = Append(index, info.Name, info is FileInfo file ? file.Length : 0, info.LastWriteTimeUtc, info.Attributes);
            if (info is DirectoryInfo)
            {
                _scanFolder(added, Path.Join(path, info.Name));
            }
        }
    }

    private int Append(int parent, ReadOnlySpan<char> name, long size, DateTime lastWriteUtc, FileAttributes attributes)
    {
        _batch.Clear();
        _batch.Add(parent, name, size, lastWriteUtc, attributes, EntryFlags.None);
        var index = _tree.Append(_batch);
        _tree.MarkChanged(index, ChangeKind.Added);
        Interlocked.Increment(ref _applied);
        return index;
    }

    // the folder of a path, found by walking its names down from the watched folder that holds it.
    private bool TryResolve(string path, out int parent, out string name)
    {
        parent = Entry.None;
        name = string.Empty;
        WatchedFolder? root = null;
        foreach (var folder in _folders)
        {
            if (IsBelow(path, folder.Path) && (root == null || folder.Path.Length > root.Path.Length))
            {
                root = folder;
            }
        }

        if (root == null || path.Length <= root.Path.Length)
            return false;

        var segments = path[(root.Path.Length + 1)..].Split(_separator, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return false;

        var current = root.Entry;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = _tree.FindChild(current, segments[i]);
            if (current == Entry.None || !_tree[current].IsContainer)
                return false;
        }

        parent = current;
        name = segments[^1];
        return true;
    }

    private void OnDriveNotified(object? sender, ChangeNotifyEventArgs e)
    {
        if (!IsEnabled || e.IdList1 is null || e.Event is not { } kind)
            return;

        using var item = ShellItem.FromPidl(e.IdList1.Pointer, throwOnError: false);
        var parsingName = item?.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING, false);
        if (item == null || parsingName == null)
            return;

        lock (_lock)
        {
            if (_disposed || _computer == Entry.None)
                return;

            var existing = Entry.None;
            for (var child = _tree[_computer].FirstChild; child != Entry.None; child = _tree[child].NextSibling)
            {
                if ((_tree[child].Flags & EntryFlags.Removed) == 0 && string.Equals(_tree.GetShellNode(child)?.ParsingName, parsingName, StringComparison.OrdinalIgnoreCase))
                {
                    existing = child;
                    break;
                }
            }

            if ((kind & _driveAddEvents) != 0)
            {
                if (existing == Entry.None)
                {
                    var index = new ShellScanner(_tree).AddChild(_computer, item);
                    _tree.MarkChanged(index, ChangeKind.Added);
                    Interlocked.Increment(ref _applied);
                }
                return;
            }

            if (existing != Entry.None)
            {
                _tree.Remove(existing);
                Interlocked.Increment(ref _applied);
            }
        }
    }

    private static bool IsBelow(string path, string folder) =>
        path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && (path.Length == folder.Length || path[folder.Length] == _separator);

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (var folder in _folders)
            {
                folder.Watcher.EnableRaisingEvents = false;
                folder.Watcher.Dispose();
            }
            _folders.Clear();

            if (_drives != null)
            {
                _drives.Notified -= OnDriveNotified;
                _drives.Dispose();
                _drives = null;
            }
        }
        _timer.Dispose();
    }

    private sealed record WatchedFolder(int Entry, string Path, FileSystemWatcher Watcher);
}
