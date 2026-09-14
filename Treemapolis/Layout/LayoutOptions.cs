namespace Treemapolis.Layout;

// what the user chose about how the map looks, as one value handed to a layout on its worker.
// elevation scales the height of the buildings, one is their natural height.
public readonly record struct LayoutOptions(ColorMode ColorMode, bool ShowHidden, float Elevation);
