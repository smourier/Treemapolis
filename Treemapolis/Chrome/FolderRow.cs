namespace Treemapolis.Chrome;

// one folder in a list the user picks from: its name, a dim line under it, its size, and how big it is next to the largest of the list.
// the current one is the folder the map already shows, or the one on the way to it.
public readonly record struct FolderRow(int Entry, string Name, string Detail, string Size, float Share, bool IsCurrent)
{
    // a typed location that is not in the tree, opened as a new map when chosen.
    public StartLocation? Start { get; init; }

    // the row that shows the shell's folder picker.
    public bool IsBrowse { get; init; }
}
