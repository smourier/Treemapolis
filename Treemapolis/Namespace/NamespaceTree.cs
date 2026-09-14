namespace Treemapolis.Namespace;

// every item of a session, whatever enumerated it. scanners append under a single lock,
// readers index up to Count without one and may see rollups a batch behind, never torn memory.
public sealed class NamespaceTree
{
    private readonly Lock _lock = new();
    private readonly ChunkedList<Entry> _entries = new();
    private readonly ChunkedList<char> _names = new();
    private ShellNode[] _shellNodes = [];
    private int _shellNodeCount;
    private int _version;
    private readonly ConcurrentDictionary<int, TreeChange> _changes = new();
    private long _lastChangeTimestamp;
    private long _removalsEndTimestamp;

    // how long a change is shown for, and how long something removed stays on the map while it sinks away.
    public const double ChangeSeconds = 3;
    public const double RemovalSeconds = 1.5;

    public int Count => _entries.Count;
    public int Version => Volatile.Read(ref _version);
    public long AllocatedBytes => _entries.AllocatedBytes + _names.AllocatedBytes;

    public ref readonly Entry this[int index] => ref _entries[index];
    public IEnumerable<KeyValuePair<int, TreeChange>> Changes => _changes;
    public long LastChangeTimestamp => Interlocked.Read(ref _lastChangeTimestamp);

    // when the last removal has sunk away, the map is laid out once more then, without it.
    public long RemovalsEndTimestamp => Interlocked.Read(ref _removalsEndTimestamp);

    public bool TryGetChange(int index, out TreeChange change) => _changes.TryGetValue(index, out change);

    public void ForgetChange(int index, TreeChange change) => _changes.TryRemove(new KeyValuePair<int, TreeChange>(index, change));

    // the first child by that name that is still there, folders and files alike.
    public int FindChild(int parent, ReadOnlySpan<char> name)
    {
        for (var child = _entries[parent].FirstChild; child != Entry.None; child = _entries[child].NextSibling)
        {
            if ((_entries[child].Flags & EntryFlags.Removed) == 0 && GetName(child).Equals(name, StringComparison.OrdinalIgnoreCase))
                return child;
        }
        return Entry.None;
    }

    public void MarkChanged(int index, ChangeKind kind)
    {
        var now = Stopwatch.GetTimestamp();
        _changes[index] = new TreeChange(kind, now);
        Interlocked.Exchange(ref _lastChangeTimestamp, now);
        Interlocked.Increment(ref _version);
    }

    public void Remove(int index)
    {
        lock (_lock)
        {
            ref var entry = ref _entries[index];
            if ((entry.Flags & EntryFlags.Removed) != 0)
                return;

            entry.Flags |= EntryFlags.Removed;
            AddToAncestors(entry.Parent, -entry.TotalSize, -(entry.DescendantCount + 1));
        }

        var end = Stopwatch.GetTimestamp() + (long)(RemovalSeconds * Stopwatch.Frequency);
        Interlocked.Exchange(ref _removalsEndTimestamp, Math.Max(RemovalsEndTimestamp, end));
        MarkChanged(index, ChangeKind.Removed);
    }

    // a folder read again, so the next time its date moves it is known to have changed once more.
    public void SetLastWrite(int index, DateTime lastWriteUtc)
    {
        lock (_lock)
        {
            _entries[index].LastWriteMinutes = Entry.ToMinutes(lastWriteUtc);
        }
    }

    // a file that changed on disk, its new size carried up to every folder above it.
    public void Update(int index, long size, DateTime lastWriteUtc)
    {
        lock (_lock)
        {
            ref var entry = ref _entries[index];
            var delta = size - entry.Size;
            entry.Size = size;
            entry.TotalSize += delta;
            entry.LastWriteMinutes = Entry.ToMinutes(lastWriteUtc);
            AddToAncestors(entry.Parent, delta, 0);
        }
        MarkChanged(index, ChangeKind.Changed);
    }

    public ReadOnlySpan<char> GetName(int index)
    {
        ref readonly var entry = ref _entries[index];
        return _names.GetSpan(entry.NameOffset, entry.NameLength);
    }

    public ShellNode? GetShellNode(int index)
    {
        var node = _entries[index].ShellNode;
        return node == Entry.None ? null : Volatile.Read(ref _shellNodes)[node];
    }

    // the file system path of an item, rebuilt from the nearest ancestor that starts a file system subtree.
    public string? GetFileSystemPath(int index)
    {
        var names = new List<int>();
        var current = index;
        while (current != Entry.None)
        {
            var shell = GetShellNode(current);
            if (shell != null)
            {
                if (shell.FileSystemPath == null)
                    return null;

                var sb = new StringBuilder(shell.FileSystemPath);
                for (var i = names.Count - 1; i >= 0; i--)
                {
                    if (sb.Length > 0 && sb[^1] != Path.DirectorySeparatorChar)
                    {
                        sb.Append(Path.DirectorySeparatorChar);
                    }
                    sb.Append(GetName(names[i]));
                }
                return sb.ToString();
            }

            names.Add(current);
            current = _entries[current].Parent;
        }
        return null;
    }

    public int AddShellItem(int parent, ReadOnlySpan<char> name, ShellNode node, EntryFlags flags, FileAttributes attributes, long size, DateTime lastWriteUtc)
    {
        ArgumentNullException.ThrowIfNull(node);
        lock (_lock)
        {
            if (_shellNodeCount == _shellNodes.Length)
            {
                var grown = new ShellNode[Math.Max(16, _shellNodes.Length * 2)];
                _shellNodes.CopyTo(grown, 0);
                Volatile.Write(ref _shellNodes, grown);
            }

            _shellNodes[_shellNodeCount] = node;
            var record = new BatchRecord
            {
                Parent = parent,
                NameLength = (ushort)Math.Min(name.Length, ushort.MaxValue),
                Flags = flags | EntryFlags.Shell,
                Attributes = attributes,
                Size = size,
                LastWriteMinutes = Entry.ToMinutes(lastWriteUtc),
            };
            var index = AppendLocked(record, name[..record.NameLength], _shellNodeCount++);
            AddToAncestors(parent, size, 1);
            Interlocked.Increment(ref _version);
            return index;
        }
    }

    // returns the index of the first record, the others follow in batch order.
    public int Append(EntryBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Count == 0)
            return Count;

        lock (_lock)
        {
            var first = _entries.Count;
            var runParent = batch[0].Parent;
            long runSize = 0;
            var runCount = 0;
            for (var i = 0; i < batch.Count; i++)
            {
                ref readonly var record = ref batch[i];
                if (record.Parent != runParent)
                {
                    AddToAncestors(runParent, runSize, runCount);
                    runParent = record.Parent;
                    runSize = 0;
                    runCount = 0;
                }

                AppendLocked(record, batch.GetName(i), Entry.None);
                runSize += record.Size;
                runCount++;
            }

            AddToAncestors(runParent, runSize, runCount);
            Interlocked.Increment(ref _version);
            return first;
        }
    }

    public void AddFlags(int index, EntryFlags flags)
    {
        lock (_lock)
        {
            _entries[index].Flags |= flags;
            Interlocked.Increment(ref _version);
        }
    }

    private int AppendLocked(in BatchRecord record, ReadOnlySpan<char> name, int shellNode)
    {
        var nameOffset = _names.AddContiguous(name);
        var entry = new Entry
        {
            Parent = record.Parent,
            FirstChild = Entry.None,
            NextSibling = Entry.None,
            NameOffset = nameOffset,
            NameLength = record.NameLength,
            ShellNode = shellNode,
            Flags = record.Flags,
            Attributes = record.Attributes,
            Size = record.Size,
            TotalSize = record.Size,
            LastWriteMinutes = record.LastWriteMinutes,
        };

        // a new child goes first in its parent's list, which links it without walking the siblings.
        if (record.Parent != Entry.None)
        {
            ref var parent = ref _entries[record.Parent];
            entry.NextSibling = parent.FirstChild;
            var index = _entries.Add(entry);
            parent.FirstChild = index;
            parent.ChildCount++;
            return index;
        }
        return _entries.Add(entry);
    }

    private void AddToAncestors(int index, long size, int count)
    {
        while (index != Entry.None)
        {
            ref var entry = ref _entries[index];
            entry.TotalSize += size;
            entry.DescendantCount += count;
            index = entry.Parent;
        }
    }
}
