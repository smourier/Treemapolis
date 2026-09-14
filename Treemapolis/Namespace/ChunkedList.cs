namespace Treemapolis.Namespace;

// append only storage in fixed chunks, so it never moves what it already holds.
// one writer adds, any number of readers index up to Count without a lock, since a chunk is never reallocated.
public sealed class ChunkedList<T> where T : unmanaged
{
    private const int _chunkShift = 16;
    private const int _chunkSize = 1 << _chunkShift;
    private const int _chunkMask = _chunkSize - 1;
    private T[][] _chunks = [];
    private int _chunkCount;
    private int _count;

    public int Count => Volatile.Read(ref _count);
    public unsafe long AllocatedBytes => (long)Volatile.Read(ref _chunkCount) * _chunkSize * sizeof(T);

    public ref T this[int index] => ref _chunks[index >> _chunkShift][index & _chunkMask];

    public int Add(in T item)
    {
        var index = _count;
        EnsureChunk(index >> _chunkShift);
        _chunks[index >> _chunkShift][index & _chunkMask] = item;
        Volatile.Write(ref _count, index + 1);
        return index;
    }

    // the items land in a single chunk, which is what lets a reader take them back as one span.
    public int AddContiguous(ReadOnlySpan<T> items)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(items.Length, _chunkSize);
        var index = _count;
        if ((index & _chunkMask) + items.Length > _chunkSize)
        {
            index = (index | _chunkMask) + 1;
        }

        EnsureChunk(index >> _chunkShift);
        items.CopyTo(_chunks[index >> _chunkShift].AsSpan(index & _chunkMask));
        Volatile.Write(ref _count, index + items.Length);
        return index;
    }

    public ReadOnlySpan<T> GetSpan(int index, int length) => _chunks[index >> _chunkShift].AsSpan(index & _chunkMask, length);

    private void EnsureChunk(int chunk)
    {
        if (chunk < _chunkCount)
            return;

        var chunks = _chunks;
        if (chunk >= chunks.Length)
        {
            var grown = new T[Math.Max(4, chunks.Length * 2)][];
            chunks.CopyTo(grown, 0);
            Volatile.Write(ref _chunks, grown);
            chunks = grown;
        }

        chunks[chunk] = GC.AllocateUninitializedArray<T>(_chunkSize);
        Volatile.Write(ref _chunkCount, chunk + 1);
    }
}
