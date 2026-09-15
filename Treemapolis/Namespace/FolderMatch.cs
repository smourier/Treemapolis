namespace Treemapolis.Namespace;

// how well a folder name matched a search, zero for the whole name, and how big the folder is.
public readonly record struct FolderMatch(int Rank, long Size);
