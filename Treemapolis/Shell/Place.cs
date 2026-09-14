namespace Treemapolis.Shell;

// one tile of the island: a drive, or a child of the Desktop, the roots Explorer lists in its tree.
public sealed class Place
{
    public required string DisplayName { get; init; }
    public required string ParsingName { get; init; }
    public byte[]? IdList { get; init; }
    public string? FileSystemPath { get; init; }
    public bool IsDrive { get; init; }
    public long Capacity { get; init; }
    public long FreeSpace { get; init; }
    public ShellImage? Image { get; init; }
}
