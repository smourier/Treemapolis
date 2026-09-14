namespace Treemapolis.Shell;

// a picture fitted in a square cell, premultiplied BGRA, with every smaller level of the cell already filtered down to a few pixels.
// no pixels means the shell had no picture for it, which is remembered too.
public sealed class Thumbnail
{
    public required int Entry { get; init; }
    public required int Generation { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public IReadOnlyList<byte[]> Levels { get; init; } = [];
}
