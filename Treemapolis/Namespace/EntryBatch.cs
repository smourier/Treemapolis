namespace Treemapolis.Namespace;

// what a scanner gathers on its own thread before taking the tree lock once for all of it.
public sealed class EntryBatch
{
    private const int _initialCapacity = 4096;
    private const int _initialNameCapacity = 64 * 1024;
    private BatchRecord[] _records = new BatchRecord[_initialCapacity];
    private char[] _names = new char[_initialNameCapacity];
    private int _nameLength;

    public int Count { get; private set; }

    public ref readonly BatchRecord this[int index] => ref _records[index];

    public ReadOnlySpan<char> GetName(int index)
    {
        ref readonly var record = ref _records[index];
        return _names.AsSpan(record.NameStart, record.NameLength);
    }

    // what the attributes already say, a folder, hidden, system, a link, becomes flags here, the caller only adds what they cannot say.
    public void Add(int parent, ReadOnlySpan<char> name, long size, DateTime lastWriteUtc, FileAttributes attributes, EntryFlags flags)
    {
        if ((attributes & FileAttributes.Directory) != 0)
        {
            flags |= EntryFlags.Container;
        }

        if ((attributes & FileAttributes.Hidden) != 0)
        {
            flags |= EntryFlags.Hidden;
        }

        if ((attributes & FileAttributes.System) != 0)
        {
            flags |= EntryFlags.System;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            flags |= EntryFlags.ReparsePoint;
        }

        if (Count == _records.Length)
        {
            Array.Resize(ref _records, _records.Length * 2);
        }

        name = name[..Math.Min(name.Length, ushort.MaxValue)];
        if (_nameLength + name.Length > _names.Length)
        {
            Array.Resize(ref _names, Math.Max(_names.Length * 2, _nameLength + name.Length));
        }

        name.CopyTo(_names.AsSpan(_nameLength));
        _records[Count++] = new BatchRecord
        {
            Parent = parent,
            NameStart = _nameLength,
            NameLength = (ushort)name.Length,
            Size = size,
            LastWriteMinutes = Entry.ToMinutes(lastWriteUtc),
            Attributes = attributes,
            Flags = flags,
        };
        _nameLength += name.Length;
    }

    public void Clear()
    {
        Count = 0;
        _nameLength = 0;
    }
}
