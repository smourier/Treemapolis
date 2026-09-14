namespace Treemapolis.Namespace;

// what happened to an entry after it was scanned, and when, as a Stopwatch timestamp.
public readonly record struct TreeChange(ChangeKind Kind, long Timestamp);
