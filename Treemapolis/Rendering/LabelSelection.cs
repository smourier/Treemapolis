namespace Treemapolis.Rendering;

// which names are worth drawing from where the camera is: the folders whose name band would be largest on screen,
// and the files of the folders close enough for their buildings to be told apart. it runs on a worker, a disk has millions of candidates.
public sealed class LabelSelection
{
    private const int _maxContainerLabels = 384;
    private const int _maxFileLabels = 256;
    private const int _maxRevealedContainers = 48;
    private const float _minimumPixels = 7;
    private const float _revealPixels = 160;
    private const float _headerTextRatio = 0.7f;
    private const float _containerWidthRatio = 0.92f;
    private const float _fileLabelRatio = 0.35f;
    private const float _maximumFileLabelHeight = 0.6f;
    private const float _fileWidthRatio = 1.1f;
    private const int _maxThumbnails = ThumbnailAtlas.CellCount / 2;

    public required LayoutSnapshot Layout { get; init; }
    public required int AtlasGeneration { get; init; }
    public IReadOnlyList<LabelCandidate> Candidates { get; private init; } = [];

    // the files whose tops are large enough on screen for a thumbnail, largest first.
    // the shell decides which have one, a file type without a thumbnail keeps its color.
    public IReadOnlyList<int> Thumbnails { get; private init; } = [];

    public static float MinimumPixels => _minimumPixels;

    public static LabelSelection Select(LayoutSnapshot layout, in Matrix4x4 viewProjection, Vector3 cameraPosition, float pixelScale, int atlasGeneration, bool showThumbnails, float minimumThumbnailPixels)
    {
        // a folder shows the names of its files once it is large on screen, and their thumbnails sooner when they are asked for smaller.
        var revealPixels = showThumbnails ? MathF.Min(_revealPixels, minimumThumbnailPixels) : _revealPixels;
        ArgumentNullException.ThrowIfNull(layout);
        var planes = FrustumPlanes.FromViewProjection(viewProjection);
        var instances = layout.Instances;
        var containers = new PriorityQueue<LabelCandidate, float>(_maxContainerLabels + 1);
        var revealed = new PriorityQueue<int, float>(_maxRevealedContainers + 1);
        for (var i = 0; i < layout.ContainerEntries.Length; i++)
        {
            var entry = layout.ContainerEntries[i];
            ref readonly var instance = ref instances[entry];
            var side = MathF.Min(instance.Size.X, instance.Size.Z);
            var top = instance.Position.Y + instance.Size.Y;
            var boxCenter = new Vector3(instance.Position.X, top, instance.Position.Z);
            if (!IsVisible(planes, boxCenter, MathF.Max(instance.Size.X, instance.Size.Z) * 0.71f))
                continue;

            var header = TreemapLayout.GetHeader(side);
            if (header > 0)
            {
                var padding = TreemapLayout.GetPadding(side);
                var height = header * _headerTextRatio;
                var inset = padding + (header - height) * 0.5f;
                var center = new Vector3(instance.Position.X, top, instance.Position.Z + instance.Size.Z * 0.5f - inset - height * 0.5f);
                var pixels = height * pixelScale / MathF.Max(Vector3.Distance(center, cameraPosition), 0.01f);
                if (pixels >= _minimumPixels && IsVisible(planes, center, instance.Size.X * 0.5f))
                {
                    Offer(containers, new LabelCandidate(entry, LabelInstance.ContainerKind, height, instance.Size.X * _containerWidthRatio, inset, pixels), pixels, _maxContainerLabels);
                }
            }

            var sidePixels = side * pixelScale / MathF.Max(Vector3.Distance(boxCenter, cameraPosition), 0.01f);
            if (sidePixels >= revealPixels && layout.ContainerFileCounts[i] > 0)
            {
                Offer(revealed, i, sidePixels, _maxRevealedContainers);
            }
        }

        var files = new PriorityQueue<LabelCandidate, float>(_maxFileLabels + 1);
        var thumbnails = new PriorityQueue<int, float>(_maxThumbnails + 1);
        while (revealed.TryDequeue(out var container, out var containerPixels))
        {
            var namesShown = containerPixels >= _revealPixels;
            var start = layout.ContainerFileStarts[container];
            var count = layout.ContainerFileCounts[container];
            for (var k = 0; k < count; k++)
            {
                var entry = layout.FileEntries[start + k];
                if (layout.AggregateInfos.ContainsKey(entry))
                    continue;

                ref readonly var instance = ref instances[entry];
                var footprint = MathF.Min(instance.Size.X, instance.Size.Z);
                var height = MathF.Min(footprint * _fileLabelRatio, _maximumFileLabelHeight);
                var center = instance.Position + new Vector3(0, instance.Size.Y + height, 0);
                if (!IsVisible(planes, center, MathF.Max(height * 4, footprint)))
                    continue;

                var distance = MathF.Max(Vector3.Distance(center, cameraPosition), 0.01f);
                var pixels = height * pixelScale / distance;
                if (namesShown && pixels >= _minimumPixels)
                {
                    Offer(files, new LabelCandidate(entry, LabelInstance.FileKind, height, footprint * _fileWidthRatio, 0, pixels), pixels, _maxFileLabels);
                }

                var topPixels = footprint * pixelScale / distance;
                if (showThumbnails && topPixels >= minimumThumbnailPixels)
                {
                    Offer(thumbnails, entry, topPixels, _maxThumbnails);
                }
            }
        }

        var candidates = new List<LabelCandidate>(containers.Count + files.Count);
        while (containers.TryDequeue(out var candidate, out _))
        {
            candidates.Add(candidate);
        }

        while (files.TryDequeue(out var candidate, out _))
        {
            candidates.Add(candidate);
        }

        // largest first, so when rasterizing runs out of budget it is the smallest names that wait.
        candidates.Sort((a, b) => b.Pixels.CompareTo(a.Pixels));
        var thumbnailEntries = new int[thumbnails.Count];
        for (var i = thumbnailEntries.Length - 1; i >= 0; i--)
        {
            thumbnailEntries[i] = thumbnails.Dequeue();
        }
        return new LabelSelection { Layout = layout, AtlasGeneration = atlasGeneration, Candidates = candidates, Thumbnails = thumbnailEntries };
    }

    // a min heap bounded to the limit keeps the largest seen so far without sorting millions.
    private static void Offer<T>(PriorityQueue<T, float> queue, T item, float priority, int limit)
    {
        if (queue.Count < limit)
        {
            queue.Enqueue(item, priority);
            return;
        }

        if (queue.TryPeek(out _, out var smallest) && priority > smallest)
        {
            queue.EnqueueDequeue(item, priority);
        }
    }

    private static bool IsVisible(in FrustumPlanes planes, Vector3 center, float radius)
    {
        foreach (var plane in planes)
        {
            if (Vector3.Dot(new Vector3(plane.X, plane.Y, plane.Z), center) + plane.W < -radius)
                return false;
        }
        return true;
    }
}
