namespace Treemapolis.Namespace;

// 56 bytes per item, a disk of millions stays within a few hundred megabytes.
[StructLayout(LayoutKind.Sequential)]
public struct Entry
{
    public const int None = -1;

    public int Parent;
    public int FirstChild;
    public int NextSibling;
    public int ChildCount;
    public int DescendantCount;
    public int NameOffset;
    public int ShellNode;
    public int LastWriteMinutes;
    public ushort NameLength;
    public EntryFlags Flags;
    public FileAttributes Attributes;
    public long Size;
    public long TotalSize;

    public readonly bool IsContainer => (Flags & EntryFlags.Container) != 0;
    public readonly DateTime LastWriteTimeUtc => new(LastWriteMinutes * TimeSpan.TicksPerMinute, DateTimeKind.Utc);

    public static int ToMinutes(DateTime utc) => (int)(utc.Ticks / TimeSpan.TicksPerMinute);
}
