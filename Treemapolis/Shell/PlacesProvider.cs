using ShellN;
using ShellN.Extensions;

namespace Treemapolis.Shell;

// the drives, then the children of the Desktop, read off the UI thread and read again when a drive comes or goes.
public sealed class PlacesProvider : IDisposable
{
    private const int _quietMilliseconds = 500;
    private const int _imageSize = 64;
    private const SHCNE_ID _driveEvents = SHCNE_ID.SHCNE_DRIVEADD | SHCNE_ID.SHCNE_DRIVEREMOVED | SHCNE_ID.SHCNE_DRIVEADDGUI | SHCNE_ID.SHCNE_MEDIAINSERTED | SHCNE_ID.SHCNE_MEDIAREMOVED;

    private readonly Action _changed;
    private readonly Timer _timer;
    private readonly ChangeNotifier _notifier = new();
    private IReadOnlyList<Place> _places = [];
    private int _version;
    private bool _disposed;

    public PlacesProvider(Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        _changed = changed;
        _timer = new Timer(_ => _ = LoadAsync(), null, 0, Timeout.Infinite);
        _notifier.Notified += OnNotified;
        _ = _notifier.Run(null, true, _driveEvents);
    }

    public IReadOnlyList<Place> Places => Volatile.Read(ref _places);
    public int Version => Volatile.Read(ref _version);

    private void OnNotified(object? sender, ChangeNotifyEventArgs e)
    {
        if (!_disposed)
        {
            _timer.Change(_quietMilliseconds, Timeout.Infinite);
        }
    }

    private async Task LoadAsync()
    {
        var places = new List<Place>();
        var localDriveRoots = ShellScanner.GetLocalDriveRoots();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!localDriveRoots.Contains(drive.Name))
                continue;

            try
            {
                using var item = ShellItem.FromParsingName(drive.Name, throwOnError: false);
                var image = item != null ? await GetImageAsync(item, drive.Name).ConfigureAwait(false) : null;
                places.Add(new Place
                {
                    DisplayName = item?.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, false) ?? drive.Name,
                    ParsingName = drive.Name,
                    IdList = item?.GetIdListAsByteArray(false),
                    FileSystemPath = drive.Name,
                    IsDrive = true,
                    Capacity = drive.IsReady ? drive.TotalSize : 0,
                    FreeSpace = drive.IsReady ? drive.AvailableFreeSpace : 0,
                    Image = image,
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Application.TraceVerbose($"'{drive.Name}' could not be described: {ex.Message}");
            }
        }

        try
        {
            // shared and cached by ShellN, not ours to dispose.
            foreach (var child in ShellFolder.Desktop.EnumerateChildren(_SHCONTF.SHCONTF_FOLDERS))
            {
                using (child)
                {
                    var name = child.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, false);
                    var parsingName = child.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING, false);
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(parsingName))
                        continue;

                    var path = child.Attributes.HasFlag(SFGAO_FLAGS.SFGAO_FILESYSTEM) ? child.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, false) : null;
                    var image = await GetImageAsync(child, parsingName).ConfigureAwait(false);
                    places.Add(new Place { DisplayName = name, ParsingName = parsingName, IdList = child.GetIdListAsByteArray(false), FileSystemPath = path, Image = image });
                }
            }
        }
        catch (Exception ex)
        {
            Application.TraceError($"the desktop's children could not be listed: {ex}");
        }

        if (_disposed)
            return;

        Volatile.Write(ref _places, places);
        Interlocked.Increment(ref _version);
        _changed();
    }

    // the picture Explorer shows for it, a thumbnail when the item has one and its icon otherwise.
    private static async Task<ShellImage?> GetImageAsync(ShellItem item, string name)
    {
        try
        {
            using var bitmap = await item.GetImageAsBitmapAsync(new SIZE(_imageSize, _imageSize), SIIGBF.SIIGBF_RESIZETOFIT, WICBitmapAlphaChannelOption.WICBitmapUsePremultipliedAlpha).ConfigureAwait(false);
            return bitmap != null ? ShellImage.FromBitmap(bitmap) : null;
        }
        catch (Exception ex)
        {
            Application.TraceVerbose($"no picture for '{name}': {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _notifier.Notified -= OnNotified;
        _notifier.Dispose();
        _timer.Dispose();
    }
}
