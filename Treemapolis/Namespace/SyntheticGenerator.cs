namespace Treemapolis.Namespace;

// a seeded tree of any size, shaped like a real disk, deep folders with a long tail of file sizes,
// so the renderer can be pushed past whatever the machine it runs on happens to hold.
public sealed class SyntheticGenerator(NamespaceTree tree)
{
    private const double _meanFolders = 3.2;
    private const double _meanFiles = 18;
    private const double _sizeMu = 10.5;
    private const double _sizeSigma = 2.6;
    private const int _yearsOfHistory = 10;
    private const int _maxNameLength = 64;
    private static readonly string[] _extensions = [".jpg", ".png", ".mp4", ".mkv", ".docx", ".pdf", ".txt", ".dll", ".exe", ".zip", ".iso", ".mp3", ".cs", ".json", ".log"];

    public int AddRoot()
    {
        var node = new ShellNode { ParsingName = Res.SyntheticRootName };
        return tree.AddShellItem(Entry.None, Res.SyntheticRootName, node, EntryFlags.Container | EntryFlags.Synthetic, FileAttributes.Directory, 0, DateTime.UtcNow);
    }

    public void Generate(int rootIndex, int entryCount, int seed, CancellationToken cancellationToken)
    {
        var random = new Random(seed);
        var batch = new EntryBatch();
        var pending = new Queue<int>();
        pending.Enqueue(rootIndex);
        var now = DateTime.UtcNow;
        var created = 0;
        var folderNumber = 0;
        var fileNumber = 0;
        Span<char> name = stackalloc char[_maxNameLength];

        while (created < entryCount && !cancellationToken.IsCancellationRequested)
        {
            // an exhausted queue restarts from the root, which only happens for tiny trees.
            var parent = pending.Count > 0 ? pending.Dequeue() : rootIndex;
            batch.Clear();

            var folders = Poisson(random, _meanFolders);
            for (var i = 0; i < folders && created < entryCount; i++, created++)
            {
                var length = Compose(name, Res.SyntheticFolderPrefix, folderNumber++, string.Empty);
                batch.Add(parent, name[..length], 0, RandomDate(random, now), FileAttributes.Directory, EntryFlags.Synthetic);
            }

            var files = Poisson(random, _meanFiles);
            for (var i = 0; i < files && created < entryCount; i++, created++)
            {
                var extension = _extensions[random.Next(_extensions.Length)];
                var length = Compose(name, Res.SyntheticFilePrefix, fileNumber++, extension);
                var size = (long)Math.Exp(_sizeMu + _sizeSigma * Gaussian(random));
                batch.Add(parent, name[..length], size, RandomDate(random, now), FileAttributes.Normal, EntryFlags.Synthetic);
            }

            var first = tree.Append(batch);
            for (var i = 0; i < batch.Count; i++)
            {
                if ((batch[i].Flags & EntryFlags.Container) != 0)
                {
                    pending.Enqueue(first + i);
                }
            }
            tree.AddFlags(parent, EntryFlags.Enumerated);
        }
    }

    private static int Compose(Span<char> destination, string prefix, int number, string suffix)
    {
        prefix.CopyTo(destination);
        number.TryFormat(destination[prefix.Length..], out var written, provider: CultureInfo.InvariantCulture);
        var length = prefix.Length + written;
        suffix.CopyTo(destination[length..]);
        return length + suffix.Length;
    }

    private static DateTime RandomDate(Random random, DateTime now)
    {
        // squaring leans towards recent dates, the way a disk that is in use looks.
        var age = random.NextDouble();
        return now.AddDays(-age * age * _yearsOfHistory * 365);
    }

    private static double Gaussian(Random random)
    {
        var u1 = 1 - random.NextDouble();
        var u2 = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    private static int Poisson(Random random, double mean)
    {
        var limit = Math.Exp(-mean);
        var product = random.NextDouble();
        var count = 0;
        while (product > limit)
        {
            product *= random.NextDouble();
            count++;
        }
        return count;
    }
}
