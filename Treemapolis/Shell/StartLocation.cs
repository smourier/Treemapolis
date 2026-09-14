namespace Treemapolis.Shell;

// what a location given on the command line opens: the folder itself, or for a file the folder holding it with the file to select.
public sealed class StartLocation
{
    public required byte[] IdList { get; init; }
    public string? SelectName { get; init; }
}
