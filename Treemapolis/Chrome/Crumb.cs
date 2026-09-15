namespace Treemapolis.Chrome;

// one folder of the path shown in the caption, from the opened location down to the folder the map is laid out from.
public readonly record struct Crumb(int Entry, string Name);
