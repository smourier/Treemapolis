namespace Treemapolis.Namespace;

[Flags]
public enum EntryFlags : ushort
{
    None = 0,
    Container = 0x1,
    Hidden = 0x2,
    System = 0x4,
    ReparsePoint = 0x8,
    Shell = 0x10,
    Drive = 0x20,
    Enumerated = 0x40,
    AccessDenied = 0x80,
    Synthetic = 0x100,

    // gone from disk or from the shell, its rollups already taken off its ancestors, left in place since the tree only ever appends.
    Removed = 0x200,
}
