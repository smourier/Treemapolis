namespace Treemapolis.Chrome;

// Segoe MDL2 Assets keeps its symbols in the private use area.
// where it is missing they come from Segoe UI Symbol, at the code points Unicode gives them.
public sealed class Glyphs
{
    private const string _modernFamily = "Segoe MDL2 Assets";
    private const string _olderFamily = "Segoe UI Symbol";

    public Glyphs(IComObject<IDWriteFactory> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var modern = HasFamily(factory, _modernFamily);
        Family = modern ? _modernFamily : _olderFamily;
        Back = modern ? (char)0xE112 : '←';
        Forward = modern ? (char)0xE111 : '→';
        Up = modern ? (char)0xE110 : '↑';
        Reveal = modern ? (char)0xE838 : '❐';
        FrameAll = modern ? (char)0xE9A6 : '⤢';
        Hidden = modern ? (char)0xE7B3 : '◉';
        ColorMode = modern ? (char)0xE790 : '◑';
        Settings = modern ? (char)0xE713 : '⚙';
        Shield = modern ? (char)0xEA18 : '⛨';
        Check = modern ? (char)0xE73E : '✓';
        Submenu = modern ? (char)0xE76C : '❯';
        Search = modern ? (char)0xE721 : '⌕';
    }

    public string Family { get; }
    public char Back { get; }
    public char Forward { get; }
    public char Up { get; }
    public char Reveal { get; }
    public char FrameAll { get; }
    public char Hidden { get; }
    public char ColorMode { get; }
    public char Settings { get; }
    public char Shield { get; }
    public char Check { get; }
    public char Submenu { get; }
    public char Search { get; }

    private static bool HasFamily(IComObject<IDWriteFactory> factory, string name)
    {
        try
        {
            using var collection = factory.GetSystemFontCollection();
            return collection.FindFamilyNameIndex(name) >= 0;
        }
        catch (Exception ex)
        {
            Application.TraceWarning($"the installed fonts could not be read ({ex.Message}), '{_olderFamily}' is assumed.");
            return false;
        }
    }
}
