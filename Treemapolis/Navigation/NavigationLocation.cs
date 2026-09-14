namespace Treemapolis.Navigation;

// where the map was: the location that was opened, by id list when the shell gave one since a parsing name does not always bind back,
// and the names from that root down to the folder the map was laid out from.
public sealed class NavigationLocation
{
    public byte[]? IdList { get; init; }
    public required string ParsingName { get; init; }
    public required string DisplayName { get; init; }
    public IReadOnlyList<string> Path { get; init; } = [];

    public bool IsSameRoot(NavigationLocation other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (IdList != null && other.IdList != null)
            return IdList.AsSpan().SequenceEqual(other.IdList);

        return ParsingName.Equals(other.ParsingName, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => Path.Count > 0 ? Path[^1] : DisplayName;
}
