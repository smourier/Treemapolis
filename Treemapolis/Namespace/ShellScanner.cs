using ShellN;
using ShellN.Extensions;

namespace Treemapolis.Namespace;

// the places only the shell can list, This PC, libraries, devices, network locations.
// every child costs a COM object and several calls, which is fine for the few items they hold and why a file system subtree never comes through here.
public sealed class ShellScanner(NamespaceTree tree)
{
    private const string _namespacePrefix = "::";
    private const string _guidFormat = "B";
    private const string _uncPrefix = @"\\";
    private const int _driveRootLength = 3;

    public static string ComputerParsingName { get; } = _namespacePrefix + ShellN.Constants.CLSID_MyComputer.ToString(_guidFormat);

    public static bool IsComputer(string? parsingName) => string.Equals(parsingName, ComputerParsingName, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlySet<string> GetLocalDriveRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Network)
                {
                    roots.Add(drive.Name);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Application.TraceWarning($"the drives could not be listed: {ex.Message}");
        }
        return roots;
    }

    public static bool IsHiddenLocation(string? path, IReadOnlySet<string> localDriveRoots)
    {
        ArgumentNullException.ThrowIfNull(localDriveRoots);
        if (path == null)
            return false;

        if (path.StartsWith(_uncPrefix, StringComparison.Ordinal))
            return true;

        return path.Length == _driveRootLength && path[1] == Path.VolumeSeparatorChar && !localDriveRoots.Contains(path);
    }

    public static bool IsHiddenLocation(ShellItem item, IReadOnlySet<string> localDriveRoots)
    {
        ArgumentNullException.ThrowIfNull(item);
        return IsHiddenLocation(item.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING, false), localDriveRoots)
            || (item.IsDrive && IsHiddenLocation(item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, false), localDriveRoots));
    }

    public int AddRoot(string parsingName)
    {
        ArgumentNullException.ThrowIfNull(parsingName);
        using var item = ShellItem.FromParsingName(parsingName, throwOnError: false) ?? ShellItem.FromSplitParsingName(parsingName, throwOnError: false)
            ?? throw new DirectoryNotFoundException(string.Format(CultureInfo.CurrentCulture, Res.LocationNotFound, parsingName));

        return Add(Entry.None, item);
    }

    public unsafe int AddRoot(byte[] idList)
    {
        ArgumentNullException.ThrowIfNull(idList);
        fixed (byte* pidl = idList)
        {
            using var item = ShellItem.FromPidl((nint)pidl, throwOnError: false) ?? throw new DirectoryNotFoundException(Res.LocationIdListNotFound);
            return Add(Entry.None, item);
        }
    }

    public unsafe void Enumerate(int index)
    {
        var node = tree.GetShellNode(index);
        if (node?.IdList == null)
            return;

        try
        {
            fixed (byte* pidl = node.IdList)
            {
                using var item = ShellItem.FromPidl((nint)pidl, throwOnError: false);
                if (item is not ShellFolder folder)
                    return;

                var flags = _SHCONTF.SHCONTF_FOLDERS | _SHCONTF.SHCONTF_NONFOLDERS | _SHCONTF.SHCONTF_INCLUDEHIDDEN;
                var localDriveRoots = IsComputer(node.ParsingName) ? GetLocalDriveRoots() : null;
                foreach (var child in folder.EnumerateChildren(flags))
                {
                    using (child)
                    {
                        if (localDriveRoots == null || !IsHiddenLocation(child, localDriveRoots))
                        {
                            Add(index, child);
                        }
                    }
                }
            }
            tree.AddFlags(index, EntryFlags.Enumerated);
        }
        catch (Exception ex)
        {
            Application.TraceWarning($"'{node.ParsingName}' could not be enumerated: {ex.Message}");
            tree.AddFlags(index, EntryFlags.Enumerated | EntryFlags.AccessDenied);
        }
    }

    public int AddChild(int parent, ShellItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Add(parent, item);
    }

    private int Add(int parent, ShellItem item)
    {
        var attributes = item.Attributes;
        var name = item.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, false) ?? string.Empty;
        var parsingName = item.GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING, false) ?? name;

        // an archive says it is a folder and a stream at once, here it stays a file.
        var isStream = attributes.HasFlag(SFGAO_FLAGS.SFGAO_STREAM);
        var isContainer = attributes.HasFlag(SFGAO_FLAGS.SFGAO_FOLDER) && !isStream;
        string? fileSystemPath = null;

        // the desktop answers with the user's Desktop directory, which is only one part of what it holds.
        if (attributes.HasFlag(SFGAO_FLAGS.SFGAO_FILESYSTEM) && item is not ShellFolder { IsDesktop: true })
        {
            fileSystemPath = item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, false);
            if (isContainer && fileSystemPath != null && !Directory.Exists(fileSystemPath))
            {
                fileSystemPath = null;
            }
        }

        var flags = isContainer ? EntryFlags.Container : EntryFlags.None;
        if (attributes.HasFlag(SFGAO_FLAGS.SFGAO_HIDDEN))
        {
            flags |= EntryFlags.Hidden;
        }

        var driveType = DriveType.Unknown;
        long capacity = 0;
        long freeSpace = 0;
        if (item.IsDrive && fileSystemPath != null)
        {
            flags |= EntryFlags.Drive;
            try
            {
                var drive = new DriveInfo(fileSystemPath);
                driveType = drive.DriveType;
                if (drive.IsReady)
                {
                    capacity = drive.TotalSize;
                    freeSpace = drive.AvailableFreeSpace;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Application.TraceVerbose($"'{fileSystemPath}' could not be described: {ex.Message}");
            }
        }

        var node = new ShellNode
        {
            ParsingName = parsingName,
            IdList = item.GetIdListAsByteArray(false),
            FileSystemPath = fileSystemPath,
            DriveType = driveType,
            DriveCapacity = capacity,
            DriveFreeSpace = freeSpace,
        };

        var size = isContainer ? 0 : item.Size ?? 0;
        var lastWrite = item.DateModified?.ToUniversalTime() ?? DateTime.MinValue;
        return tree.AddShellItem(parent, name, node, flags, isContainer ? FileAttributes.Directory : FileAttributes.Normal, size, lastWrite);
    }
}
