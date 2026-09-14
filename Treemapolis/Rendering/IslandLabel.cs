namespace Treemapolis.Rendering;

// the place of a tile and where its top face is on screen, for the chrome to write on it and draw its picture.
public readonly record struct IslandLabel(Place Place, string? Detail, D2D_RECT_F Bounds);
