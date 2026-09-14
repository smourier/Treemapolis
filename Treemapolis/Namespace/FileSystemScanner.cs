namespace Treemapolis.Namespace;

// FindFirstFileEx underneath, through FileSystemEnumerable, whose entry is a ref struct over the raw find data,
// so nothing is allocated per file. directories go to a shared queue drained by as many threads as the disk keeps up with.
public sealed class FileSystemScanner(NamespaceTree tree)
{
    private const int _flushThreshold = 4096;

    private static readonly EnumerationOptions _options = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

    public static int DefaultWorkerCount => Math.Clamp(Environment.ProcessorCount, 4, 32);

    public Task ScanAsync(IReadOnlyList<DirectoryWork> roots, bool recursive, int workerCount, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (roots.Count == 0)
            return Task.CompletedTask;

        var job = new ScanJob(tree, recursive, cancellationToken);
        foreach (var root in roots)
        {
            job.Enqueue(root);
        }

        for (var i = 0; i < Math.Max(1, workerCount); i++)
        {
            new Thread(job.Run) { IsBackground = true, Name = nameof(FileSystemScanner) }.Start();
        }
        return job.Completion;
    }

    private sealed class ScanJob(NamespaceTree tree, bool recursive, CancellationToken cancellationToken)
    {
        private readonly ConcurrentQueue<DirectoryWork> _queue = new();
        private readonly SemaphoreSlim _available = new(0);
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _outstanding;
        private int _workers;

        public Task Completion => _completion.Task;

        public void Enqueue(DirectoryWork work)
        {
            Interlocked.Increment(ref _outstanding);
            _queue.Enqueue(work);
            _available.Release();
        }

        public void Run()
        {
            Interlocked.Increment(ref _workers);
            var batch = new EntryBatch();
            try
            {
                while (!_completion.Task.IsCompleted)
                {
                    _available.Wait();
                    if (!_queue.TryDequeue(out var work))
                        continue;

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        Enumerate(work, batch);
                    }

                    if (Interlocked.Decrement(ref _outstanding) == 0)
                    {
                        _completion.TrySetResult();

                        // every other worker is parked on the semaphore and needs a wake up to see the job is done.
                        _available.Release(Volatile.Read(ref _workers));
                    }
                }
            }
            catch (Exception ex)
            {
                Application.TraceError(ex.ToString());
                _completion.TrySetException(ex);
                _available.Release(Volatile.Read(ref _workers));
            }
        }

        private void Enumerate(DirectoryWork work, EntryBatch batch)
        {
            batch.Clear();
            try
            {
                var enumerable = new FileSystemEnumerable<byte>(work.Path, (ref entry) =>
                {
                    batch.Add(work.Index, entry.FileName, entry.IsDirectory ? 0 : entry.Length, entry.LastWriteTimeUtc.UtcDateTime, entry.Attributes, EntryFlags.None);
                    if (batch.Count >= _flushThreshold)
                    {
                        Flush(work, batch);
                    }
                    return 0;
                }, _options);

                foreach (var _ in enumerable)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;
                }

                Flush(work, batch);
                tree.AddFlags(work.Index, EntryFlags.Enumerated);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                Flush(work, batch);
                tree.AddFlags(work.Index, EntryFlags.Enumerated | EntryFlags.AccessDenied);
            }
        }

        private void Flush(DirectoryWork work, EntryBatch batch)
        {
            if (batch.Count == 0)
                return;

            var first = tree.Append(batch);
            if (recursive)
            {
                for (var i = 0; i < batch.Count; i++)
                {
                    // a junction or a symbolic link would walk the same files twice, or forever.
                    ref readonly var record = ref batch[i];
                    if ((record.Flags & (EntryFlags.Container | EntryFlags.ReparsePoint)) == EntryFlags.Container)
                    {
                        Enqueue(new DirectoryWork(first + i, Path.Join(work.Path, batch.GetName(i))));
                    }
                }
            }
            batch.Clear();
        }
    }
}
