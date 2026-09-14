namespace Treemapolis.Chrome;

// one row of the settings menu. the menu knows nothing about settings, a row says what it shows and what it does.
public sealed class MenuEntry
{
    public static MenuEntry Separator { get; } = new() { Label = string.Empty, Kind = MenuEntryKind.Separator };

    public required string Label { get; init; }
    public MenuEntryKind Kind { get; init; }

    // read every frame, so what a row shows follows the setting.
    public Func<string>? Value { get; init; }
    public Func<bool>? Checked { get; init; }
    public Func<bool>? Enabled { get; init; }
    public Action? Invoked { get; init; }

    // read when the submenu opens, so a list that changes, the recent folders, is right every time it is shown.
    public Func<IReadOnlyList<MenuEntry>>? Children { get; init; }

    // a command is done with once it runs, a toggle is not, several are often changed in a row.
    public bool ClosesMenu { get; init; }

    public double Minimum { get; init; }
    public double Maximum { get; init; }
    public double Step { get; init; } = 1;
    public Func<double>? Number { get; init; }
    public Action<double>? SetNumber { get; init; }

    public bool IsInteractive => Kind != MenuEntryKind.Separator && Enabled?.Invoke() != false;
}
