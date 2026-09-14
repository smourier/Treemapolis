namespace Treemapolis.Namespace;

// decides what gets enumerated and when. a file system root is scanned whole, a shell location two levels deep,
// and anything remote or slow waits until it is expanded, so opening This PC never waits on a network drive.
// a whole NTFS drive is read from its master file table when the process may open the volume, and walked otherwise.
public sealed class NamespaceExplorer : IDisposable
{
    private Session _session = new();
    private bool _watchChanges = true;

    public NamespaceTree Tree => _session.Tree;
    public int Root => _session.Root;
    public bool IsBusy => Volatile.Read(ref _session.Pending) > 0;
    public TimeSpan Elapsed => _session.Watch.Elapsed;
    public Exception? LastError => _session.LastError;
    public MftScanner? MasterFileTable => _session.MasterFileTable;
    public string? MasterFileTableDrive => _session.MasterFileTableDrive;
    public string? WalkedDrive => _session.WalkedDrive;
    public VolumeAccess? MasterFileTableAccess => _session.MasterFileTableAccess;
    public Exception? MasterFileTableError => _session.MasterFileTableError;
    public int WorkerCount { get; set; } = FileSystemScanner.DefaultWorkerCount;
    public ChangeWatcher? Watcher => _session.Watcher;

    // changes on disk and drives coming and going are applied to the tree while this is on.
    public bool WatchChanges
    {
        get => _watchChanges;
        set
        {
            _watchChanges = value;
            _session.Watcher?.IsEnabled = value;
        }
    }

    public void Open(string? location) => Open(shell => shell.AddRoot(string.IsNullOrWhiteSpace(location) ? ShellScanner.ComputerParsingName : location));

    // a parsing name does not always bind back to what gave it, the desktop's is the user's Desktop directory.
    public void Open(byte[] idList)
    {
        ArgumentNullException.ThrowIfNull(idList);
        Open(shell => shell.AddRoot(idList));
    }

    private void Open(Func<ShellScanner, int> addRoot)
    {
        var session = BeginSession();
        Track(session, async () =>
        {
            var tree = session.Tree;
            var shell = new ShellScanner(tree);
            session.Root = addRoot(shell);
            var watcher = new ChangeWatcher(tree, (index, path) => ScanAddedFolder(session, index, path)) { IsEnabled = _watchChanges };
            session.Watcher = watcher;
            if (session.Token.IsCancellationRequested)
            {
                watcher.Dispose();
                return;
            }

            var rootPath = tree.GetFileSystemPath(session.Root);
            if (rootPath != null)
            {
                session.RecursiveRoots[session.Root] = 0;
                watcher.WatchFolder(session.Root, rootPath);
                if (!TryScanMasterFileTable(session, session.Root, rootPath))
                {
                    await new FileSystemScanner(tree).ScanAsync([new DirectoryWork(session.Root, rootPath)], true, WorkerCount, session.Token).ConfigureAwait(false);
                }
                return;
            }

            shell.Enumerate(session.Root);
            if (tree.GetShellNode(session.Root)?.ParsingName == ShellScanner.ComputerParsingName)
            {
                watcher.WatchDrives(session.Root);
            }

            var listings = new List<DirectoryWork>();
            for (var child = tree[session.Root].FirstChild; child != Entry.None; child = tree[child].NextSibling)
            {
                var node = tree.GetShellNode(child);
                if (node?.ParsingName == ShellScanner.ComputerParsingName)
                {
                    watcher.WatchDrives(child);
                }

                if (!tree[child].IsContainer || node?.FileSystemPath == null || node.IsRemote)
                    continue;

                if ((tree[child].Flags & EntryFlags.Drive) != 0 && node.DriveCapacity == 0)
                    continue;

                listings.Add(new DirectoryWork(child, node.FileSystemPath));
            }
            await new FileSystemScanner(tree).ScanAsync(listings, false, WorkerCount, session.Token).ConfigureAwait(false);
        });
    }

    public void OpenSynthetic(int entryCount, int seed)
    {
        var session = BeginSession();
        Track(session, () =>
        {
            var generator = new SyntheticGenerator(session.Tree);
            session.Root = generator.AddRoot();
            generator.Generate(session.Root, entryCount, seed, session.Token);
            return Task.CompletedTask;
        });
    }

    public void Expand(int index)
    {
        var session = _session;
        var tree = session.Tree;
        if (index < 0 || index >= tree.Count || !tree[index].IsContainer || (tree[index].Flags & EntryFlags.Synthetic) != 0)
            return;

        // a subtree already on its way down is not scanned twice, the second scan would append every entry again.
        for (var ancestor = index; ancestor != Entry.None; ancestor = tree[ancestor].Parent)
        {
            if (session.RecursiveRoots.ContainsKey(ancestor))
                return;
        }

        var path = tree.GetFileSystemPath(index);
        if (path != null)
        {
            session.RecursiveRoots[index] = 0;
            session.Watcher?.WatchFolder(index, path);
        }

        Track(session, async () =>
        {
            if (path == null)
            {
                if ((tree[index].Flags & EntryFlags.Enumerated) == 0)
                {
                    new ShellScanner(tree).Enumerate(index);
                }
                return;
            }

            if (TryScanMasterFileTable(session, index, path))
                return;

            var roots = new List<DirectoryWork>();
            if ((tree[index].Flags & EntryFlags.Enumerated) == 0)
            {
                roots.Add(new DirectoryWork(index, path));
            }
            else
            {
                // listed already but not below, the scan goes on from the folders it holds.
                for (var child = tree[index].FirstChild; child != Entry.None; child = tree[child].NextSibling)
                {
                    ref readonly var entry = ref tree[child];
                    if (entry.IsContainer && (entry.Flags & (EntryFlags.Enumerated | EntryFlags.ReparsePoint | EntryFlags.Removed)) == 0)
                    {
                        var childPath = tree.GetFileSystemPath(child);
                        if (childPath != null)
                        {
                            roots.Add(new DirectoryWork(child, childPath));
                        }
                    }
                }
            }
            await new FileSystemScanner(tree).ScanAsync(roots, true, WorkerCount, session.Token).ConfigureAwait(false);
        });
    }

    private static bool TryScanMasterFileTable(Session session, int index, string path)
    {
        if (!MftScanner.IsWholeDrive(path))
            return false;

        var scanner = new MftScanner(session.Tree);
        session.MasterFileTable = scanner;
        session.MasterFileTableDrive = path;
        VolumeAccess access;
        try
        {
            access = scanner.Scan(index, path, session.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // nothing reaches the tree before the whole table is read, so a failed read leaves the walk a clean start.
            Application.TraceError(ex.ToString());
            session.MasterFileTableError = ex;
            access = VolumeAccess.ReadError;
        }

        session.MasterFileTableAccess = access;
        if (access == VolumeAccess.Granted)
            return true;

        session.MasterFileTable = null;
        session.MasterFileTableDrive = null;
        if (access is not VolumeAccess.NotAWholeDrive and not VolumeAccess.NotNtfs and not VolumeAccess.NotReady)
        {
            session.WalkedDrive = path;
        }
        return false;
    }

    private void ScanAddedFolder(Session session, int index, string path)
    {
        if (session.Token.IsCancellationRequested)
            return;

        Track(session, () => new FileSystemScanner(session.Tree).ScanAsync([new DirectoryWork(index, path)], true, WorkerCount, session.Token));
    }

    private Session BeginSession()
    {
        var previous = Interlocked.Exchange(ref _session, new Session());
        previous.Cancel();
        return _session;
    }

    private static void Track(Session session, Func<Task> operation)
    {
        if (Interlocked.Increment(ref session.Pending) == 1)
        {
            session.Watch.Restart();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await operation().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Application.TraceError(ex.ToString());
                session.LastError = ex;
            }
            finally
            {
                if (Interlocked.Decrement(ref session.Pending) == 0)
                {
                    session.Watch.Stop();
                }
            }
        });
    }

    public void Dispose() => _session.Cancel();

    // everything one opened location owns, so a cancelled one still finishing never touches the next.
    private sealed class Session
    {
        private readonly CancellationTokenSource _cancellation = new();

        public int Pending;

        public NamespaceTree Tree { get; } = new();
        public ConcurrentDictionary<int, byte> RecursiveRoots { get; } = new();
        public Stopwatch Watch { get; } = new();
        public CancellationToken Token => _cancellation.Token;
        public int Root { get; set; } = Entry.None;
        public Exception? LastError { get; set; }
        public ChangeWatcher? Watcher { get; set; }
        public MftScanner? MasterFileTable { get; set; }
        public string? MasterFileTableDrive { get; set; }
        public string? WalkedDrive { get; set; }
        public VolumeAccess? MasterFileTableAccess { get; set; }
        public Exception? MasterFileTableError { get; set; }

        public void Cancel()
        {
            _cancellation.Cancel();
            Watcher?.Dispose();
        }
    }
}
