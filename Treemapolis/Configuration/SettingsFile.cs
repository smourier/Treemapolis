namespace Treemapolis.Configuration;

// a file next to the executable wins, which is what makes a copied folder portable,
// and it is used only when it is already there, an ordinary install writes under the user's local application data.
public sealed class SettingsFile
{
    private const string _fileName = "treemapolis.settings.json";
    private const string _folderName = "Treemapolis";
    private const string _temporaryExtension = ".tmp";
    private const int _maximumRecentFolders = 20;

    // one lock for the file while it is written, one for the queue of writes, which the UI thread takes and must never wait on a disk for.
    private readonly Lock _lock = new();
    private readonly Lock _queueLock = new();
    private Task _writes = Task.CompletedTask;

    // fresh settings start from the defaults and are never written, the file keeps what it had.
    public SettingsFile(string? location = null, bool fresh = false)
    {
        IsFresh = fresh;
        if (location != null)
        {
            Location = Path.GetFullPath(location);
            return;
        }

        var executable = Environment.ProcessPath;
        var folder = executable == null ? null : Path.GetDirectoryName(executable);
        var beside = folder == null ? null : Path.Join(folder, _fileName);
        Location = beside != null && File.Exists(beside) ? beside : Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), _folderName, _fileName);
    }

    public string Location { get; }
    public bool IsFresh { get; }

    public Settings Load()
    {
        if (IsFresh)
            return new Settings();

        try
        {
            if (File.Exists(Location))
            {
                using var stream = File.OpenRead(Location);
                var loaded = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.Settings);
                if (loaded != null)
                {
                    loaded.RecentFolders.Sort((left, right) => right.LastVisited.CompareTo(left.LastVisited));
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            // a settings file that cannot be read is no reason to refuse to start, the defaults are all sound.
            Application.TraceError($"'{Location}' could not be read: {ex}");
        }
        return new Settings();
    }

    // on the way out, where there is no later.
    public void Save(Settings settings) => Write(Snapshot(settings));

    // the settings become bytes on the thread that owns them and only the writing is handed away, chained so the last snapshot is the last written.
    public void SaveLater(Settings settings)
    {
        if (IsFresh)
            return;

        var bytes = Snapshot(settings);
        if (bytes == null)
            return;

        lock (_queueLock)
        {
            _writes = _writes.ContinueWith(_ => Write(bytes), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    public static void RememberFolder(Settings settings, string parsingName, string displayName)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrEmpty(parsingName))
            return;

        settings.RecentFolders.RemoveAll(folder => folder.ParsingName.Equals(parsingName, StringComparison.OrdinalIgnoreCase));
        settings.RecentFolders.Insert(0, new RecentFolder { ParsingName = parsingName, DisplayName = displayName, LastVisited = DateTime.Now });
        if (settings.RecentFolders.Count > _maximumRecentFolders)
        {
            settings.RecentFolders.RemoveRange(_maximumRecentFolders, settings.RecentFolders.Count - _maximumRecentFolders);
        }
    }

    private static byte[]? Snapshot(Settings settings)
    {
        try
        {
            return JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJsonContext.Default.Settings);
        }
        catch (Exception ex)
        {
            Application.TraceError($"the settings could not be serialized: {ex}");
            return null;
        }
    }

    private void Write(byte[]? bytes)
    {
        if (bytes == null || IsFresh)
            return;

        try
        {
            lock (_lock)
            {
                var folder = Path.GetDirectoryName(Location);
                if (folder != null)
                {
                    Directory.CreateDirectory(folder);
                }

                var temporary = Location + _temporaryExtension;
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, Location, true);
            }
        }
        catch (Exception ex)
        {
            Application.TraceError($"'{Location}' could not be written: {ex}");
        }
    }
}
