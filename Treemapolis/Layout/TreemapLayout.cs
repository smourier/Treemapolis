namespace Treemapolis.Layout;

// a squarified treemap in three dimensions. every folder is a slab standing on the slab of its parent, so nesting reads as terraces,
// its children share its top in proportion to their size, and a file is a building whose height grows with the logarithm of its size.
// the map is laid out whole, the GPU drops what is too small to see, so flying down reveals deeper levels on its own.
// what would be smaller than a readable tile is gathered into one block per folder, and a folder too small to open stays a closed slab,
// diving into it makes it the root of a new map with room for all of it.
public sealed class TreemapLayout
{
    public const float MapSize = 1024;

    private const float _minimumTileSide = 0.3f;
    private const float _minimumOpenSide = 1.5f;
    private const int _instanceBudget = 4_000_000;
    private const float _terraceRatio = 0.012f;
    private const float _minimumTerrace = 0.03f;
    private const float _maximumTerrace = 2.5f;
    private const float _paddingRatio = 0.015f;
    private const float _minimumPadding = 0.02f;
    private const float _maximumPadding = 3;
    private const float _headerRatio = 0.06f;
    private const float _maximumHeader = 24;
    private const float _minimumHeaderSide = 4;
    private const float _buildingMarginRatio = 0.1f;
    private const float _buildingBaseRatio = 0.12f;
    private const float _buildingRatioPerDecade = 0.1f;
    private const float _maximumBuildingRatio = 1.2f;

    // a file that owns half the map is a wide low block, never a tower hiding everything around it.
    private const float _maximumBuildingHeight = 16;

    // a flat map still lifts its tiles a hair above the folder they stand on, coplanar faces would flicker.
    private const float _minimumBuildingHeight = 0.02f;
    private const int _changeKindShift = 30;
    private const uint _changeTimeMask = 0x3FFFFFFF;
    private const float _aggregateRatio = 0.12f;
    private const double _minimumWeightShare = 1e-6;
    private const double _unsizedContainerShare = 0.02;
    private const double _kilobyte = 1024;
    private const float _hiddenDimming = 0.55f;
    private const uint _driveColor = 0x5B84B1;
    private const uint _aggregateColor = 0x5F6673;
    private const uint _noExtensionColor = 0x8A8F99;

    // images, video, audio, archives and disk images, code, documents, executables and libraries.
    private static readonly uint[] _familyColors = [0x4CC38A, 0xE0567A, 0xB57BE8, 0xE8A13C, 0x4FA8E8, 0xE8DE5A, 0xE86B45];
    private static readonly uint[] _otherColors = [0x7FB7BE, 0xC7A27C, 0x9DBF6B, 0xB38FB5, 0xD9B35F, 0x8FA3C7];
    private static readonly Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> _familyLookup = CreateFamilyLookup();

    // the slabs darken level after level, so the depth of a terrace can be read at a glance.
    private static readonly uint[] _containerPalette = [0x2E4A66, 0x35577A, 0x3D6488, 0x467294, 0x507F9E, 0x5B8BA6, 0x6797AD, 0x73A2B3];

    // newest to oldest: a day, a week, a month, a year, three years, and older.
    private static readonly uint[] _agePalette = [0xFF4040, 0xFF9A3C, 0xFFE15A, 0x7FD36B, 0x4FB3C8, 0x6C79C9];
    private static readonly int[] _ageLimitsMinutes = [60 * 24, 60 * 24 * 7, 60 * 24 * 30, 60 * 24 * 365, 60 * 24 * 365 * 3];

    // the colors this layout paints with and what they mean, one owner for both the map and its legend.
    public static IReadOnlyList<LegendEntry> GetLegend(ColorMode colorMode)
    {
        var entries = new List<LegendEntry>();
        if (colorMode == ColorMode.Age)
        {
            string[] ages = [Res.LegendAgeDay, Res.LegendAgeWeek, Res.LegendAgeMonth, Res.LegendAgeYear, Res.LegendAgeThreeYears, Res.LegendAgeOlder];
            for (var i = 0; i < ages.Length; i++)
            {
                entries.Add(new LegendEntry(_agePalette[i], _agePalette[i], ages[i]));
            }
        }
        else
        {
            string[] families = [Res.LegendImages, Res.LegendVideo, Res.LegendAudio, Res.LegendArchives, Res.LegendCode, Res.LegendDocuments, Res.LegendPrograms];
            for (var i = 0; i < families.Length; i++)
            {
                entries.Add(new LegendEntry(_familyColors[i], _familyColors[i], families[i]));
            }
            entries.Add(new LegendEntry(_otherColors[0], _otherColors[^1], Res.LegendOtherTypes));
            entries.Add(new LegendEntry(_noExtensionColor, _noExtensionColor, Res.LegendNoExtension));
        }

        entries.Add(new LegendEntry(_aggregateColor, _aggregateColor, Res.LegendAggregate));
        entries.Add(new LegendEntry(_containerPalette[0], _containerPalette[^1], Res.LegendFolders));
        entries.Add(new LegendEntry(_driveColor, _driveColor, Res.LegendDrives));
        entries.Add(new LegendEntry(Dim(_familyColors[4], EntryFlags.Hidden), Dim(_familyColors[4], EntryFlags.Hidden), Res.LegendHidden));
        return entries;
    }

    public static float GetTerrace(float side) => Math.Clamp(side * _terraceRatio, _minimumTerrace, _maximumTerrace);
    public static float GetPadding(float side) => Math.Clamp(side * _paddingRatio, _minimumPadding, _maximumPadding);
    public static float GetHeader(float side) => side < _minimumHeaderSide ? 0 : MathF.Min(side * _headerRatio, _maximumHeader);

    public static LayoutSnapshot Build(NamespaceTree tree, int root, LayoutOptions options, int settingsVersion, long clockEpoch, CancellationToken cancellationToken)
    {
        var buildTimestamp = Stopwatch.GetTimestamp();
        var colorMode = options.ColorMode;
        var elevation = Math.Max(0, options.Elevation);
        ArgumentNullException.ThrowIfNull(tree);
        var start = Stopwatch.GetTimestamp();
        var treeVersion = tree.Version;
        var count = tree.Count;
        var instances = new TargetInstance[count];
        if (root < 0 || root >= count)
            return new LayoutSnapshot { Tree = tree, Root = root, Instances = instances, Count = count, TreeVersion = treeVersion, SettingsVersion = settingsVersion };

        var now = Entry.ToMinutes(DateTime.UtcNow);
        var queue = new Queue<Work>();
        queue.Enqueue(new Work(root, Entry.None, -MapSize * 0.5f, -MapSize * 0.5f, MapSize * 0.5f, MapSize * 0.5f, 0, 0));
        var containers = new List<int>();
        var fileStarts = new List<int>();
        var fileCounts = new List<int>();
        var files = new List<int>();
        var aggregates = new Dictionary<int, AggregateInfo>();
        var items = new List<Item>();
        var weights = new List<double>();
        var rects = new List<Rect>();
        var boundsMin = new Vector3(float.MaxValue);
        var boundsMax = new Vector3(float.MinValue);
        var emitted = 0;

        while (queue.TryDequeue(out var work))
        {
            if ((emitted & 0xFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            ref readonly var entry = ref tree[work.Entry];
            var width = work.X1 - work.X0;
            var depth = work.Z1 - work.Z0;
            var side = MathF.Min(width, depth);
            var terrace = GetTerrace(side);
            var top = work.Base + terrace;
            var flags = InstanceFlags.Visible | InstanceFlags.Container | HiddenFlag(entry.Flags);
            var color = _containerPalette[Math.Min(work.Depth, _containerPalette.Length - 1)];
            if ((entry.Flags & EntryFlags.Drive) != 0)
            {
                flags |= InstanceFlags.Drive;
                color = _driveColor;
            }

            containers.Add(work.Entry);
            fileStarts.Add(files.Count);
            fileCounts.Add(0);
            emitted++;
            boundsMin = Vector3.Min(boundsMin, new Vector3(work.X0, work.Base, work.Z0));
            boundsMax = Vector3.Max(boundsMax, new Vector3(work.X1, top, work.Z1));

            var padding = GetPadding(side);
            var header = GetHeader(side);
            var inner = new Rect(work.X0 + padding, work.Z0 + padding, work.X1 - padding, work.Z1 - padding - header);
            var open = entry.ChildCount > 0 && side >= _minimumOpenSide && emitted < _instanceBudget && inner.Width > 0 && inner.Depth > 0;
            if (entry.ChildCount > 0 && !open)
            {
                flags |= InstanceFlags.Collapsed;
            }

            instances[work.Entry] = new TargetInstance
            {
                Position = new Vector3((work.X0 + work.X1) * 0.5f, work.Base, (work.Z0 + work.Z1) * 0.5f),
                Size = new Vector3(width, terrace, depth),
                Color = Dim(color, entry.Flags),
                Flags = flags,
                Parent = work.Parent,
            };

            if (!open)
                continue;

            GatherChildren(tree, work.Entry, count, options.ShowHidden, buildTimestamp, items);
            if (items.Count == 0)
                continue;

            // largest first, the squarified rows depend on it and the aggregation cuts the tail.
            items.Sort((a, b) => b.Weight.CompareTo(a.Weight));
            double total = 0;
            foreach (var item in items)
            {
                total += item.Weight;
            }

            // the smallest items are gathered once their tile would drop under a readable side, and all of the ones after them too.
            var innerArea = (double)inner.Width * inner.Depth;
            var kept = items.Count;
            for (var i = 0; i < items.Count; i++)
            {
                if (Math.Sqrt(items[i].Weight / total * innerArea) < _minimumTileSide)
                {
                    kept = i;
                    break;
                }
            }

            if (kept == items.Count - 1)
            {
                kept = items.Count;
            }

            weights.Clear();
            for (var i = 0; i < kept; i++)
            {
                weights.Add(items[i].Weight);
            }

            long aggregateSize = 0;
            var aggregateEntry = Entry.None;
            if (kept < items.Count)
            {
                aggregateEntry = items[kept].Entry;
                double aggregateWeight = 0;
                for (var i = kept; i < items.Count; i++)
                {
                    aggregateWeight += items[i].Weight;
                    aggregateSize += items[i].IsContainer ? tree[items[i].Entry].TotalSize : tree[items[i].Entry].Size;
                }
                weights.Add(aggregateWeight);
            }

            rects.Clear();
            Squarify(CollectionsMarshal.AsSpan(weights), total, inner, rects);

            var containerIndex = containers.Count - 1;
            fileStarts[containerIndex] = files.Count;
            for (var i = 0; i < rects.Count; i++)
            {
                var rect = rects[i];
                if (i == kept)
                {
                    EmitBuilding(instances, aggregateEntry, work.Entry, rect, top, _aggregateColor, InstanceFlags.Aggregate, _aggregateRatio, elevation);
                    aggregates[aggregateEntry] = new AggregateInfo(work.Entry, items.Count - kept, aggregateSize);
                    files.Add(aggregateEntry);
                    emitted++;
                    continue;
                }

                var item = items[i];

                // something removed keeps its tile and sinks flat into it, a folder included, what it held already gone.
                if (item.IsRemoving)
                {
                    var removedColor = item.IsContainer ? _containerPalette[Math.Min(work.Depth + 1, _containerPalette.Length - 1)] : TypeColor(tree.GetName(item.Entry));
                    EmitBuilding(instances, item.Entry, work.Entry, rect, top, removedColor, InstanceFlags.None, 0, 0);
                    files.Add(item.Entry);
                    emitted++;
                    continue;
                }

                if (item.IsContainer)
                {
                    queue.Enqueue(new Work(item.Entry, work.Entry, rect.X0, rect.Z0, rect.X1, rect.Z1, top, work.Depth + 1));
                    continue;
                }

                ref readonly var file = ref tree[item.Entry];
                var fileColor = colorMode == ColorMode.Age ? AgeColor(now - file.LastWriteMinutes) : TypeColor(tree.GetName(item.Entry));
                var ratio = _buildingBaseRatio + _buildingRatioPerDecade * (float)Math.Log10(1 + file.Size / _kilobyte);
                EmitBuilding(instances, item.Entry, work.Entry, rect, top, Dim(fileColor, file.Flags), HiddenFlag(file.Flags), MathF.Min(ratio, _maximumBuildingRatio), elevation);
                files.Add(item.Entry);
                emitted++;
            }
            fileCounts[containerIndex] = files.Count - fileStarts[containerIndex];
        }

        StampChanges(tree, instances, buildTimestamp, clockEpoch);
        return new LayoutSnapshot
        {
            Tree = tree,
            Root = root,
            Instances = instances,
            Count = count,
            TreeVersion = treeVersion,
            SettingsVersion = settingsVersion,
            BoundsMin = boundsMin,
            BoundsMax = boundsMax,
            Containers = containers.Count,
            Files = files.Count - aggregates.Count,
            Aggregates = aggregates.Count,
            BuildMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
            ContainerEntries = [.. containers],
            ContainerFileStarts = [.. fileStarts],
            ContainerFileCounts = [.. fileCounts],
            FileEntries = [.. files],
            AggregateInfos = aggregates,
        };
    }

    // an index past the snapshot count was appended after the layout started, it waits for the next one.
    // a change still showing is stamped on the instance of its entry, the shaders animate it from there, the old ones are forgotten.
    private static void StampChanges(NamespaceTree tree, TargetInstance[] instances, long now, long clockEpoch)
    {
        var window = (long)(NamespaceTree.ChangeSeconds * Stopwatch.Frequency);
        foreach (var (entry, change) in tree.Changes)
        {
            if (now - change.Timestamp > window)
            {
                tree.ForgetChange(entry, change);
                continue;
            }

            if (entry >= instances.Length || (instances[entry].Flags & InstanceFlags.Visible) == 0)
                continue;

            var milliseconds = (ulong)((change.Timestamp - clockEpoch) * 1000 / Stopwatch.Frequency);
            instances[entry].Change = (uint)change.Kind << _changeKindShift | (uint)(milliseconds & _changeTimeMask);
        }
    }

    private static void GatherChildren(NamespaceTree tree, int container, int count, bool showHidden, long now, List<Item> items)
    {
        items.Clear();
        double total = 0;
        for (var child = tree[container].FirstChild; child != Entry.None; child = tree[child].NextSibling)
        {
            if (child >= count)
                continue;

            // a hidden item, and a super hidden one being hidden too, is left out unless hidden items are asked for.
            if (!showHidden && (tree[child].Flags & EntryFlags.Hidden) != 0)
                continue;

            var isRemoving = false;
            if ((tree[child].Flags & EntryFlags.Removed) != 0)
            {
                if (!tree.TryGetChange(child, out var change) || change.Kind != ChangeKind.Removed || now - change.Timestamp > (long)(NamespaceTree.RemovalSeconds * Stopwatch.Frequency))
                    continue;

                isRemoving = true;
            }

            ref readonly var entry = ref tree[child];
            double weight = entry.IsContainer ? entry.TotalSize : entry.Size;

            // a drive not scanned yet is as large as what it holds, not as small as what was listed so far.
            var node = entry.ShellNode != Entry.None ? tree.GetShellNode(child) : null;
            if (node != null && node.DriveCapacity > 0)
            {
                weight = Math.Max(weight, node.DriveCapacity - node.DriveFreeSpace);
            }

            var isUnsized = entry.IsContainer && (node != null || (entry.Flags & EntryFlags.Enumerated) == 0);
            items.Add(new Item(child, weight, entry.IsContainer, isUnsized, isRemoving));
            total += weight;
        }

        // nothing sized still needs a tile, an empty folder or a network place would otherwise vanish from the map,
        // and a folder whose content is not known yet, a shell place or one only listed so far, gets a tile worth diving into.
        var floor = total <= 0 ? 1 : total * _minimumWeightShare;
        var unsizedFloor = total <= 0 ? 1 : total * _unsizedContainerShare;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var minimum = item.IsUnsized ? unsizedFloor : floor;
            if (item.Weight < minimum)
            {
                items[i] = item with { Weight = minimum };
            }
        }
    }

    private static void EmitBuilding(TargetInstance[] instances, int entry, int container, Rect rect, float top, uint color, InstanceFlags flags, float heightRatio, float elevation)
    {
        var margin = MathF.Min(rect.Width, rect.Depth) * _buildingMarginRatio;
        var width = rect.Width - 2 * margin;
        var depth = rect.Depth - 2 * margin;
        var footprint = MathF.Min(width, depth);
        instances[entry] = new TargetInstance
        {
            Position = new Vector3((rect.X0 + rect.X1) * 0.5f, top, (rect.Z0 + rect.Z1) * 0.5f),
            Size = new Vector3(width, MathF.Max(MathF.Min(footprint * heightRatio, _maximumBuildingHeight) * elevation, _minimumBuildingHeight), depth),
            Color = color,
            Flags = InstanceFlags.Visible | flags,
            Parent = container,
        };
    }

    // Bruls, Huizing and van Wijk. items are laid in rows along the shorter side of what is left,
    // a row keeps growing as long as that does not make its worst aspect ratio worse.
    private static void Squarify(ReadOnlySpan<double> weights, double total, Rect rect, List<Rect> output)
    {
        var scale = rect.Width * (double)rect.Depth / total;
        double x0 = rect.X0, z0 = rect.Z0, x1 = rect.X1, z1 = rect.Z1;
        var start = 0;
        while (start < weights.Length)
        {
            var width = x1 - x0;
            var depth = z1 - z0;
            var shortSide = Math.Max(Math.Min(width, depth), 1e-9);
            var sum = weights[start] * scale;
            var minimum = sum;
            var maximum = sum;
            var worst = Worst(sum, minimum, maximum, shortSide);
            var end = start + 1;
            while (end < weights.Length)
            {
                var area = weights[end] * scale;
                var candidate = Worst(sum + area, Math.Min(minimum, area), Math.Max(maximum, area), shortSide);
                if (candidate > worst)
                    break;

                sum += area;
                minimum = Math.Min(minimum, area);
                maximum = Math.Max(maximum, area);
                worst = candidate;
                end++;
            }

            var last = end == weights.Length;
            var thickness = sum / shortSide;
            if (width >= depth)
            {
                // a column on the left, its items stacked along z.
                var columnEnd = last ? x1 : x0 + thickness;
                var position = z0;
                for (var i = start; i < end; i++)
                {
                    var next = i == end - 1 ? z1 : position + weights[i] * scale / thickness;
                    output.Add(new Rect((float)x0, (float)position, (float)columnEnd, (float)next));
                    position = next;
                }
                x0 = columnEnd;
            }
            else
            {
                // a row at the back, its items side by side along x.
                var rowEnd = last ? z1 : z0 + thickness;
                var position = x0;
                for (var i = start; i < end; i++)
                {
                    var next = i == end - 1 ? x1 : position + weights[i] * scale / thickness;
                    output.Add(new Rect((float)position, (float)z0, (float)next, (float)rowEnd));
                    position = next;
                }
                z0 = rowEnd;
            }
            start = end;
        }
    }

    private static double Worst(double sum, double minimum, double maximum, double side)
    {
        var sideSquared = side * side;
        var sumSquared = sum * sum;
        return Math.Max(sideSquared * maximum / sumSquared, sumSquared / (sideSquared * minimum));
    }

    // a few families people recognize at a glance, the rest keep a stable color of their own derived from the extension.
    private static uint TypeColor(ReadOnlySpan<char> name)
    {
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1)
            return _noExtensionColor;

        var extension = name[(dot + 1)..];
        if (_familyLookup.TryGetValue(extension, out var family))
            return _familyColors[family];

        uint hash = 2166136261;
        foreach (var c in extension)
        {
            hash = (hash ^ char.ToLowerInvariant(c)) * 16777619;
        }
        return _otherColors[hash % (uint)_otherColors.Length];
    }

    private static InstanceFlags HiddenFlag(EntryFlags flags) => (flags & (EntryFlags.Hidden | EntryFlags.System)) != 0 ? InstanceFlags.Hidden : InstanceFlags.None;

    private static uint AgeColor(int ageMinutes)
    {
        for (var i = 0; i < _ageLimitsMinutes.Length; i++)
        {
            if (ageMinutes < _ageLimitsMinutes[i])
                return _agePalette[i];
        }
        return _agePalette[^1];
    }

    private static uint Dim(uint color, EntryFlags flags)
    {
        if ((flags & (EntryFlags.Hidden | EntryFlags.System)) == 0)
            return color;

        var r = (uint)(((color >> 16) & 0xFF) * _hiddenDimming);
        var g = (uint)(((color >> 8) & 0xFF) * _hiddenDimming);
        var b = (uint)((color & 0xFF) * _hiddenDimming);
        return r << 16 | g << 8 | b;
    }

    private readonly record struct Work(int Entry, int Parent, float X0, float Z0, float X1, float Z1, float Base, int Depth);
    private readonly record struct Item(int Entry, double Weight, bool IsContainer, bool IsUnsized, bool IsRemoving);

    private readonly record struct Rect(float X0, float Z0, float X1, float Z1)
    {
        public float Width => X1 - X0;
        public float Depth => Z1 - Z0;
    }

    private static Dictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> CreateFamilyLookup()
    {
        string[][] families =
        [
            ["jpg", "jpeg", "png", "gif", "bmp", "tif", "tiff", "webp", "heic", "avif", "svg", "ico", "raw", "cr2", "nef", "dng", "psd"],
            ["mp4", "mkv", "avi", "mov", "wmv", "webm", "m4v", "mpg", "mpeg", "flv", "ts"],
            ["mp3", "wav", "flac", "aac", "ogg", "wma", "m4a", "opus"],
            ["zip", "7z", "rar", "gz", "tar", "bz2", "xz", "cab", "iso", "vhd", "vhdx", "vmdk", "wim", "msi", "nupkg"],
            ["cs", "c", "cpp", "h", "hpp", "js", "ts", "py", "java", "rs", "go", "hlsl", "hlsli", "json", "xml", "xaml", "csproj", "sln", "slnx", "props", "targets", "yml", "yaml", "ps1", "bat", "cmd", "sh", "md", "html", "css"],
            ["pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "rtf", "odt", "csv", "epub"],
            ["exe", "dll", "sys", "pdb", "lib", "obj", "winmd", "mui", "ocx", "cpl", "drv"],
        ];

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var family = 0; family < families.Length; family++)
        {
            foreach (var extension in families[family])
            {
                map.TryAdd(extension, family);
            }
        }
        return map.GetAlternateLookup<ReadOnlySpan<char>>();
    }
}
