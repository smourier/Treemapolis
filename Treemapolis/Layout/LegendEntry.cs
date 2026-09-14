namespace Treemapolis.Layout;

// a color of the map and what it stands for, a gradient runs from Color to EndColor.
public readonly record struct LegendEntry(uint Color, uint EndColor, string Label);
