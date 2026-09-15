namespace Treemapolis.Chrome;

// one folder on a ring of the folder wheel, from its start angle over its sweep in radians. no entry stands for the small ones merged.
public readonly record struct WheelArc(int Entry, float Start, float Sweep);
