namespace Treemapolis.Configuration;

public sealed class RecentFolder
{
    public string ParsingName { get; set; } = string.Empty;

    // what to show for it, so the list is drawn without binding every entry through the shell, "This PC" rather than its class id.
    public string DisplayName { get; set; } = string.Empty;

    public DateTime LastVisited { get; set; } = DateTime.Now;

    public override string ToString() => DisplayName.Length > 0 ? DisplayName : ParsingName;
}
