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

    // This PC goes on to scan its local drives whole, one after the other, once their roots are listed.
    public bool ScanDrives { get; set; } = true;

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

            if (session.ShellListings.TryAdd(session.Root, 0))
            {
                shell.Enumerate(session.Root);
            }

            if (ShellScanner.IsComputer(tree.GetShellNode(session.Root)?.ParsingName))
            {
                watcher.WatchDrives(session.Root);
            }

            var listings = new List<DirectoryWork>();
            var drives = new List<DirectoryWork>();
            for (var child = tree[session.Root].FirstChild; child != Entry.None; child = tree[child].NextSibling)
            {
                var node = tree.GetShellNode(child);
                if (ShellScanner.IsComputer(node?.ParsingName))
                {
                    watcher.WatchDrives(child);
                }

                if (!tree[child].IsContainer || node?.FileSystemPath == null || node.IsRemote)
                    continue;

                if ((tree[child].Flags & EntryFlags.Drive) != 0)
                {
                    if (node.DriveCapacity == 0)
                        continue;

                    // a disc spins up and reads slowly, it waits until it is dived into.
                    if (node.DriveType != DriveType.CDRom)
                    {
                        drives.Add(new DirectoryWork(child, node.FileSystemPath));
                    }
                }
                listings.Add(new DirectoryWork(child, node.FileSystemPath));
            }
            await new FileSystemScanner(tree).ScanAsync(listings, false, WorkerCount, session.Token).ConfigureAwait(false);

            // one drive at a time, so the disks are not all read at once, and a drive dived into meanwhile is not scanned twice.
            if (!ScanDrives || !ShellScanner.IsComputer(tree.GetShellNode(session.Root)?.ParsingName))
                return;

            // the drive Windows runs from first, it is the one most looked at, then the others in the order of their letters.
            var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
            foreach (var drive in drives.OrderBy(drive => !string.Equals(Path.GetPathRoot(drive.Path), systemRoot, StringComparison.OrdinalIgnoreCase)).ThenBy(drive => drive.Path, StringComparer.OrdinalIgnoreCase))
            {
                if (session.Token.IsCancellationRequested)
                    return;

                if (ClaimSubtree(session, drive.Index, drive.Path))
                {
                    await ScanSubtreeAsync(session, drive.Index, drive.Path).ConfigureAwait(false);
                }
            }
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

        var path = tree.GetFileSystemPath(index);
        if (path != null)
        {
            if (ClaimSubtree(session, index, path))
            {
                Track(session, () => ScanSubtreeAsync(session, index, path));
            }
            return;
        }

        if ((tree[index].Flags & EntryFlags.Enumerated) != 0 || !session.ShellListings.TryAdd(index, 0))
            return;

        Track(session, () =>
        {
            new ShellScanner(tree).Enumerate(index);
            return Task.CompletedTask;
        });
    }

    // a subtree already on its way down is not scanned twice, the second scan would append every entry again.
    private static bool ClaimSubtree(Session session, int index, string path)
    {
        var tree = session.Tree;
        for (var ancestor = tree[index].Parent; ancestor != Entry.None; ancestor = tree[ancestor].Parent)
        {
            if (session.RecursiveRoots.ContainsKey(ancestor))
                return false;
        }

        if (!session.RecursiveRoots.TryAdd(index, 0))
            return false;

        session.Watcher?.WatchFolder(index, path);
        return true;
    }

    private async Task ScanSubtreeAsync(Session session, int index, string path)
    {
        if (TryScanMasterFileTable(session, index, path))
            return;

        var tree = session.Tree;
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
        public ConcurrentDictionary<int, byte> ShellListings { get; } = new();
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
