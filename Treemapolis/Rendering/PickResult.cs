namespace Treemapolis.Rendering;

// Entry is Entry.None when nothing was under the point.
public readonly record struct PickResult(PickRequest Request, int Entry);
