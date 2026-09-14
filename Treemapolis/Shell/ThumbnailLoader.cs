using ShellN;
using ShellN.Extensions;

namespace Treemapolis.Shell;

// asks the shell for pictures on a few background workers, newest request first, the camera having moved on from the older ones.
// only bytes leave a worker, a shell object never crosses threads.
public sealed class ThumbnailLoader : IDisposable
{
    public const int CellSize = 128;
    public const int LevelCount = 5;

    private const int _bytesPerPixel = ShellImage.BytesPerPixel;
    private const int _minimumWorkers = 2;
    private const int _maximumWorkers = 4;

    private readonly ConcurrentStack<ThumbnailRequest> _requests = new();
    private readonly ConcurrentQueue<Thumbnail> _results = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Action _ready;
    private int _generation;
    private int _loaded;

    public ThumbnailLoader(Action ready)
    {
        ArgumentNullException.ThrowIfNull(ready);
        _ready = ready;
        var workers = Math.Clamp(Environment.ProcessorCount / 2, _minimumWorkers, _maximumWorkers);
        for (var i = 0; i < workers; i++)
        {
            _ = Task.Run(WorkAsync);
        }
    }

    public int Generation => Volatile.Read(ref _generation);
    public int Loaded => Volatile.Read(ref _loaded);
    public bool IsBusy => !_requests.IsEmpty;

    // what was asked for another tree is dropped, and so is whatever of it is still waiting.
    public void Reset()
    {
        Interlocked.Increment(ref _generation);
        _requests.Clear();
        _results.Clear();
    }

    public void Request(ThumbnailRequest request)
    {
        _requests.Push(request);
        _signal.Release();
    }

    public bool TryTake([NotNullWhen(true)] out Thumbnail? thumbnail) => _results.TryDequeue(out thumbnail);

    private async Task WorkAsync()
    {
        var token = _cancellation.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(token).ConfigureAwait(false);
                if (!_requests.TryPop(out var request) || request.Generation != Generation)
                    continue;

                var thumbnail = await LoadAsync(request).ConfigureAwait(false);
                if (request.Generation != Generation)
                    continue;

                _results.Enqueue(thumbnail);
                Interlocked.Increment(ref _loaded);
                _ready();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Application.TraceWarning($"a thumbnail could not be made: {ex.Message}");
            }
        }
    }

    // what the shell has cached comes first, and is fetched properly only when the cache has nothing, or something smaller than a cell.
    private static async Task<Thumbnail> LoadAsync(ThumbnailRequest request)
    {
        var empty = new Thumbnail { Entry = request.Entry, Generation = request.Generation };
        using var item = Parse(request.Path);
        if (item == null)
            return empty;

        var size = new SIZE(CellSize, CellSize);
        const SIIGBF flags = SIIGBF.SIIGBF_RESIZETOFIT | SIIGBF.SIIGBF_THUMBNAILONLY;
        using (var cached = await item.GetImageAsBitmapAsync(size, flags | SIIGBF.SIIGBF_INCACHEONLY, WICBitmapAlphaChannelOption.WICBitmapUsePremultipliedAlpha).ConfigureAwait(false))
        {
            if (cached != null)
            {
                var cachedSize = cached.GetSizeU();
                if (cachedSize.width >= CellSize || cachedSize.height >= CellSize)
                    return ToThumbnail(request, cached);
            }
        }

        // the asynchronous form copes with E_PENDING, which the shell returns while it is still extracting the picture.
        using var bitmap = await item.GetImageAsBitmapAsync(size, flags, WICBitmapAlphaChannelOption.WICBitmapUsePremultipliedAlpha).ConfigureAwait(false);
        return bitmap == null ? empty : ToThumbnail(request, bitmap);
    }

    // a folder with a namespace of its own, the Fonts folder for one, may not parse the names of its files back,
    // those are then bound as plain files through file system bind data, which is how their thumbnail handler is reached.
    private static ShellItem? Parse(string path)
    {
        var item = ShellItem.FromParsingName(path, throwOnError: false);
        if (item != null)
            return item;

        using var context = IBindCtxExtensions.CreateBindCtx(path, attributes: FileAttributes.Normal, throwOnError: false);
        return ShellItem.FromParsingName(path, context?.Object, throwOnError: false);
    }

    private static Thumbnail ToThumbnail(ThumbnailRequest request, IComObject<IWICBitmap> bitmap)
    {
        var image = ShellImage.FromBitmap(bitmap);
        var width = Math.Min(image.Width, CellSize);
        var height = Math.Min(image.Height, CellSize);
        var pixels = image.Pixels;

        // the picture sits in the corner of its cell, the cell's own smaller levels are box filtered from it,
        // so a thumbnail seen from far away shimmers no more than the blocks around it.
        var levels = new byte[LevelCount][];
        levels[0] = new byte[CellSize * CellSize * _bytesPerPixel];
        for (var y = 0; y < height; y++)
        {
            pixels.AsSpan(y * image.Width * _bytesPerPixel, width * _bytesPerPixel).CopyTo(levels[0].AsSpan(y * CellSize * _bytesPerPixel));
        }

        for (var level = 1; level < LevelCount; level++)
        {
            var side = CellSize >> level;
            var source = levels[level - 1];
            var destination = new byte[side * side * _bytesPerPixel];
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    for (var channel = 0; channel < _bytesPerPixel; channel++)
                    {
                        var sum = source[(y * 2 * side * 2 + x * 2) * _bytesPerPixel + channel]
                            + source[(y * 2 * side * 2 + x * 2 + 1) * _bytesPerPixel + channel]
                            + source[((y * 2 + 1) * side * 2 + x * 2) * _bytesPerPixel + channel]
                            + source[((y * 2 + 1) * side * 2 + x * 2 + 1) * _bytesPerPixel + channel];
                        destination[(y * side + x) * _bytesPerPixel + channel] = (byte)((sum + 2) / 4);
                    }
                }
            }
            levels[level] = destination;
        }
        return new Thumbnail { Entry = request.Entry, Generation = request.Generation, Width = width, Height = height, Levels = levels };
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _requests.Clear();
    }
}
