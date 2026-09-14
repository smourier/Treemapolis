namespace Treemapolis.Namespace;

// what the shell knows about an item the file system alone cannot describe, or that is the start of a file system subtree.
public sealed class ShellNode
{
    private const string _uncPrefix = @"\\";

    public required string ParsingName { get; init; }

    // the id list is kept because a parsing name does not always bind back, a portable device refuses its own.
    public byte[]? IdList { get; init; }
    public string? FileSystemPath { get; init; }
    public DriveType DriveType { get; init; }
    public long DriveCapacity { get; init; }
    public long DriveFreeSpace { get; init; }

    public bool IsRemote => DriveType == DriveType.Network || (FileSystemPath?.StartsWith(_uncPrefix, StringComparison.Ordinal) ?? false);
}
