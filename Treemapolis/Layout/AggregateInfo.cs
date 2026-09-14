namespace Treemapolis.Layout;

// the items of a folder too small to get a tile of their own, drawn as one block that stands for all of them.
public readonly record struct AggregateInfo(int Container, int Count, long Size);
