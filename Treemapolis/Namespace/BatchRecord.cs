namespace Treemapolis.Namespace;

[StructLayout(LayoutKind.Sequential)]
public struct BatchRecord
{
    public int Parent;
    public int NameStart;
    public int LastWriteMinutes;
    public ushort NameLength;
    public EntryFlags Flags;
    public FileAttributes Attributes;
    public long Size;
}
