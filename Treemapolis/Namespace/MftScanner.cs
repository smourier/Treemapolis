namespace Treemapolis.Namespace;

// reads a whole NTFS drive from its Master File Table rather than walking its directories.
// the table is a flat array of records, one per file, each naming its parent, so a drive that takes tens of seconds to walk
// is read in one sequential pass. it needs the volume opened raw, which Windows only allows an administrator to do.
public sealed class MftScanner(NamespaceTree tree)
{
    private const long _rootRecord = 5;
    private const long _firstUserRecord = 16;
    private const int _recordsPerBlock = 1024;
    private const int _flushThreshold = 4096;
    private const uint _attributeStandardInformation = 0x10;
    private const uint _attributeFileName = 0x30;
    private const uint _attributeData = 0x80;
    private const uint _attributeEnd = 0xFFFFFFFF;
    private const ushort _flagInUse = 0x0001;
    private const ushort _flagDirectory = 0x0002;
    private const byte _nameTypeDos = 2;
    private const long _recordNumberMask = 0x0000FFFFFFFFFFFF;
    private const string _volumePrefix = @"\\.\";
    private const string _ntfs = "NTFS";
    private const string _separators = @"\/";

#pragma warning disable IDE1006 // Naming Styles
    private const uint FSCTL_GET_NTFS_VOLUME_DATA = 0x00090064;
#pragma warning restore IDE1006

    private long _recordsRead;
    private long _recordCount;

    public long RecordsRead => Interlocked.Read(ref _recordsRead);
    public long RecordCount => Interlocked.Read(ref _recordCount);

    public static bool IsWholeDrive(string path) => GetDriveName(path) != null;

    // "C:" for "C:\", a drive root keeps its separator through Path.TrimEndingDirectorySeparator.
    private static string? GetDriveName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var trimmed = path.AsSpan().TrimEnd(_separators);
        return trimmed.Length == 2 && trimmed[1] == Path.VolumeSeparatorChar && char.IsAsciiLetter(trimmed[0]) ? trimmed.ToString() : null;
    }

    // appends the whole drive below the entry that stands for its root. entries a listing already added at the top level are kept and filled,
    // so diving into a drive This PC listed does not show its first level twice.
    public VolumeAccess Scan(int rootIndex, string driveRoot, CancellationToken cancellationToken)
    {
        var access = OpenVolume(driveRoot, out var volume, out var data);
        if (access != VolumeAccess.Granted)
            return access;

        using (volume)
        {
            var recordSize = (int)data.BytesPerFileRecordSegment;
            var sectorSize = (int)data.BytesPerSector;
            if (recordSize <= 0 || sectorSize <= 0 || data.MftValidDataLength < recordSize)
                return VolumeAccess.NoVolumeData;

            var extents = ReadMftExtents(volume!, data, recordSize);
            if (extents == null || extents.Count == 0)
                return VolumeAccess.NoTableExtents;

            var total = data.MftValidDataLength / recordSize;

            Interlocked.Exchange(ref _recordCount, total);
            var records = new Record[total];
            var names = new ChunkedList<char>();
            ReadRecords(volume!, extents, recordSize, sectorSize, records, names, cancellationToken);
            Append(rootIndex, records, names, cancellationToken);
        }
        return VolumeAccess.Granted;
    }

    private static VolumeAccess OpenVolume(string driveRoot, out SafeFileHandle? volume, out NTFS_VOLUME_DATA_BUFFER data)
    {
        volume = null;
        data = default;
        var drive = GetDriveName(driveRoot);
        if (drive == null)
            return VolumeAccess.NotAWholeDrive;

        try
        {
            if (!new DriveInfo(drive).DriveFormat.Equals(_ntfs, StringComparison.OrdinalIgnoreCase))
                return VolumeAccess.NotNtfs;

            // "\\.\C:" is the volume itself, "\\.\C:\" would be its root directory.
            volume = File.OpenHandle(_volumePrefix + drive, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (UnauthorizedAccessException)
        {
            return VolumeAccess.Denied;
        }
        catch (IOException)
        {
            return VolumeAccess.NotReady;
        }

        if (!TryGetVolumeData(volume, out data))
        {
            volume.Dispose();
            volume = null;
            return VolumeAccess.NoVolumeData;
        }
        return VolumeAccess.Granted;
    }

    private static unsafe bool TryGetVolumeData(SafeFileHandle volume, out NTFS_VOLUME_DATA_BUFFER data)
    {
        const int bufferSize = 512;
        var buffer = stackalloc byte[bufferSize];
        uint returned = 0;
        data = default;
        if (!Functions.DeviceIoControl(new HANDLE(volume.DangerousGetHandle()), FSCTL_GET_NTFS_VOLUME_DATA, 0, 0, (nint)buffer, bufferSize, (nint)(&returned), 0) ||
            returned < sizeof(NTFS_VOLUME_DATA_BUFFER))
            return false;

        data = Unsafe.ReadUnaligned<NTFS_VOLUME_DATA_BUFFER>(buffer);
        return true;
    }

    private void ReadRecords(SafeFileHandle volume, List<Extent> extents, int recordSize, int sectorSize, Record[] records, ChunkedList<char> names, CancellationToken cancellationToken)
    {
        var block = new byte[AlignToSector(recordSize * _recordsPerBlock, sectorSize)];
        var extensions = new List<Extension>();
        long index = 0;
        foreach (var extent in extents)
        {
            var remaining = extent.Length;
            var position = extent.Offset;
            while (remaining >= recordSize && index < records.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var wanted = (int)Math.Min(Math.Min(block.Length, remaining), AlignToSector((records.Length - index) * recordSize, sectorSize));
                var read = ReadFully(volume, block.AsSpan(0, wanted), position);
                if (read < recordSize)
                    break;

                var parsed = (int)Math.Min(read / recordSize, records.Length - index);
                for (var i = 0; i < parsed; i++, index++)
                {
                    ParseRecord(block.AsSpan(i * recordSize, recordSize), index, ref records[index], names, extensions);
                }

                position += (long)parsed * recordSize;
                remaining -= (long)parsed * recordSize;
                Interlocked.Exchange(ref _recordsRead, index);
            }
        }
        Interlocked.Exchange(ref _recordCount, index);

        // a file too fragmented for one record keeps its data attribute in an extension record, its size belongs to the base record.
        foreach (var extension in extensions)
        {
            if (extension.BaseRecord < records.Length && records[extension.BaseRecord].Size == 0)
            {
                records[extension.BaseRecord].Size = extension.Size;
            }
        }
    }

    // the records name their parents, so the tree is rebuilt breadth first from the root, a folder always appended before what it holds.
    private void Append(int rootIndex, Record[] records, ChunkedList<char> names, CancellationToken cancellationToken)
    {
        var childCounts = new int[records.Length + 1];
        for (long i = _firstUserRecord; i < records.Length; i++)
        {
            ref readonly var record = ref records[i];
            if (record.IsListed && record.Parent >= 0 && record.Parent < records.Length && record.Parent != i)
            {
                childCounts[record.Parent + 1]++;
            }
        }

        for (var i = 1; i < childCounts.Length; i++)
        {
            childCounts[i] += childCounts[i - 1];
        }

        var children = new int[childCounts[^1]];
        var cursor = (int[])childCounts.Clone();
        for (long i = _firstUserRecord; i < records.Length; i++)
        {
            ref readonly var record = ref records[i];
            if (record.IsListed && record.Parent >= 0 && record.Parent < records.Length && record.Parent != i)
            {
                children[cursor[record.Parent]++] = (int)i;
            }
        }

        // what a listing already put below the root is reused by name.
        var existing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var child = tree[rootIndex].FirstChild; child != Entry.None; child = tree[child].NextSibling)
        {
            existing.TryAdd(tree.GetName(child).ToString(), child);
        }

        var queue = new Queue<Directory>();
        queue.Enqueue(new Directory(_rootRecord, rootIndex));
        var batch = new EntryBatch();
        var batchDirectories = new List<Directory>();
        while (queue.Count > 0 || batch.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (queue.Count == 0 || batch.Count >= _flushThreshold)
            {
                Flush(batch, batchDirectories, queue);
                continue;
            }

            var directory = queue.Dequeue();
            for (var k = childCounts[directory.Record]; k < childCounts[directory.Record + 1]; k++)
            {
                var number = children[k];
                ref readonly var record = ref records[number];
                var name = names.GetSpan(record.NameOffset, record.NameLength);
                if (directory.Index == rootIndex && existing.TryGetValue(name.ToString(), out var listed))
                {
                    // a folder a walk already went into is left to that walk.
                    if (record.IsDirectory && (tree[listed].Flags & (EntryFlags.Container | EntryFlags.Enumerated)) == EntryFlags.Container)
                    {
                        tree.AddFlags(listed, EntryFlags.Enumerated);
                        queue.Enqueue(new Directory(number, listed));
                    }
                    continue;
                }

                if (record.IsDirectory)
                {
                    batchDirectories.Add(new Directory(number, batch.Count));
                    batch.Add(directory.Index, name, 0, DateTime.FromFileTimeUtc(Math.Max(0, record.LastWrite)), record.Attributes | FileAttributes.Directory, EntryFlags.Enumerated);
                    continue;
                }
                batch.Add(directory.Index, name, record.Size, DateTime.FromFileTimeUtc(Math.Max(0, record.LastWrite)), record.Attributes & ~FileAttributes.Directory, EntryFlags.None);
            }
        }
        tree.AddFlags(rootIndex, EntryFlags.Enumerated);
    }

    // a folder's index is only known once its batch is appended, that is when it can be queued for its own children.
    private void Flush(EntryBatch batch, List<Directory> batchDirectories, Queue<Directory> queue)
    {
        if (batch.Count > 0)
        {
            var first = tree.Append(batch);
            foreach (var directory in batchDirectories)
            {
                queue.Enqueue(new Directory(directory.Record, first + directory.Index));
            }
        }
        batch.Clear();
        batchDirectories.Clear();
    }

    // record 0 is $MFT itself, its unnamed data attribute lists where the pieces of the table are on the volume.
    private static List<Extent>? ReadMftExtents(SafeFileHandle volume, NTFS_VOLUME_DATA_BUFFER data, int recordSize)
    {
        var record = new byte[AlignToSector(recordSize, (int)data.BytesPerSector)];
        if (ReadFully(volume, record, data.MftStartLcn * data.BytesPerCluster) < recordSize)
            return null;

        var span = record.AsSpan(0, recordSize);
        if (!IsFileRecord(span) || !ApplyFixups(span))
            return null;

        int offset = BinaryPrimitives.ReadUInt16LittleEndian(span[0x14..]);
        while (offset + 8 <= span.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            if (type == _attributeEnd)
                break;

            var length = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]);
            if (length == 0 || offset + length > span.Length)
                break;

            var attribute = span.Slice(offset, (int)length);
            if (type == _attributeData && attribute[8] != 0 && attribute[9] == 0)
                return ParseRuns(attribute, data);

            offset += (int)length;
        }
        return null;
    }

    // a run list is pairs of length and offset, each preceded by a byte giving their widths, the offset relative to the previous run and signed.
    private static List<Extent> ParseRuns(Span<byte> attribute, NTFS_VOLUME_DATA_BUFFER data)
    {
        var extents = new List<Extent>();
        var runsOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x20..]);
        var lcn = 0L;
        for (int i = runsOffset; i < attribute.Length && attribute[i] != 0;)
        {
            var header = attribute[i++];
            var lengthSize = header & 0x0F;
            var offsetSize = (header >> 4) & 0x0F;
            if (lengthSize == 0 || i + lengthSize + offsetSize > attribute.Length)
                break;

            var runLength = ReadVariable(attribute.Slice(i, lengthSize), false);
            i += lengthSize;
            if (offsetSize == 0)
                continue;

            lcn += ReadVariable(attribute.Slice(i, offsetSize), true);
            i += offsetSize;
            extents.Add(new Extent(lcn * data.BytesPerCluster, runLength * data.BytesPerCluster));
        }
        return extents;
    }

    private static long ReadVariable(Span<byte> bytes, bool signed)
    {
        long value = 0;
        for (var i = bytes.Length - 1; i >= 0; i--)
        {
            value = (value << 8) | bytes[i];
        }

        if (signed && bytes.Length > 0 && (bytes[^1] & 0x80) != 0)
        {
            value -= 1L << (bytes.Length * 8);
        }
        return value;
    }

    // a raw volume only reads whole sectors, and a record can be smaller than a sector.
    private static long AlignToSector(long size, int sectorSize) => (size + sectorSize - 1) / sectorSize * sectorSize;

    private static int ReadFully(SafeFileHandle volume, Span<byte> buffer, long offset)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = RandomAccess.Read(volume, buffer[read..], offset + read);
            if (n <= 0)
                break;

            read += n;
        }
        return read;
    }

    private static bool IsFileRecord(Span<byte> record) => record.Length >= 48 && record[0] == 'F' && record[1] == 'I' && record[2] == 'L' && record[3] == 'E';

    private static void ParseRecord(Span<byte> span, long number, ref Record record, ChunkedList<char> names, List<Extension> extensions)
    {
        record.Parent = -1;
        if (!IsFileRecord(span) || !ApplyFixups(span))
            return;

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(span[0x16..]);
        if ((flags & _flagInUse) == 0)
            return;

        var baseRecord = BinaryPrimitives.ReadInt64LittleEndian(span[0x20..]) & _recordNumberMask;
        record.IsDirectory = (flags & _flagDirectory) != 0;
        int offset = BinaryPrimitives.ReadUInt16LittleEndian(span[0x14..]);
        var nameStart = 0;
        var nameLength = 0;
        var nameType = byte.MaxValue;
        while (offset + 8 <= span.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(span[offset..]);
            if (type == _attributeEnd)
                break;

            var length = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 4)..]);
            if (length == 0 || offset + length > span.Length)
                break;

            var attribute = span.Slice(offset, (int)length);
            var nonResident = attribute[8] != 0;
            if (type == _attributeStandardInformation && !nonResident)
            {
                var value = attribute[BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x14..])..];
                if (value.Length >= 0x24)
                {
                    record.LastWrite = BinaryPrimitives.ReadInt64LittleEndian(value[0x08..]);
                    record.Attributes = (FileAttributes)BinaryPrimitives.ReadUInt32LittleEndian(value[0x20..]);
                }
            }
            else if (type == _attributeFileName && !nonResident)
            {
                var value = attribute[BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x14..])..];
                if (value.Length >= 0x42)
                {
                    var length8 = value[0x40];
                    var kind = value[0x41];

                    // a long name and its 8.3 alias are two attributes, the alias never replaces the long name.
                    if ((kind != _nameTypeDos || nameType == byte.MaxValue) && 0x42 + length8 * 2 <= value.Length)
                    {
                        record.Parent = BinaryPrimitives.ReadInt64LittleEndian(value) & _recordNumberMask;
                        nameStart = offset + BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x14..]) + 0x42;
                        nameLength = length8;
                        nameType = kind;
                    }
                }
            }
            else if (type == _attributeData && attribute[9] == 0 && record.Size == 0)
            {
                // the unnamed data attribute is the content, a named one is an alternate stream Explorer does not count either.
                record.Size = nonResident ? BinaryPrimitives.ReadInt64LittleEndian(attribute[0x30..]) : BinaryPrimitives.ReadUInt32LittleEndian(attribute[0x10..]);
            }
            offset += (int)length;
        }

        if (baseRecord != 0)
        {
            if (record.Size > 0)
            {
                extensions.Add(new Extension(baseRecord, record.Size));
            }
            record.Parent = -1;
            return;
        }

        if (nameLength > 0 && (number >= _firstUserRecord || number == _rootRecord))
        {
            var name = MemoryMarshal.Cast<byte, char>(span.Slice(nameStart, nameLength * 2));
            record.NameOffset = names.AddContiguous(name);
            record.NameLength = (ushort)nameLength;
            record.IsListed = true;
        }
    }

    // the last two bytes of every sector hold a check value, the real bytes live in the update sequence array at the top of the record.
    private static bool ApplyFixups(Span<byte> record)
    {
        var arrayOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        var arrayCount = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
        if (arrayCount == 0 || arrayOffset + arrayCount * 2 > record.Length)
            return false;

        var check = BinaryPrimitives.ReadUInt16LittleEndian(record[arrayOffset..]);
        for (var i = 1; i < arrayCount; i++)
        {
            var end = i * 512 - 2;
            if (end + 2 > record.Length || BinaryPrimitives.ReadUInt16LittleEndian(record[end..]) != check)
                return false;

            BinaryPrimitives.WriteUInt16LittleEndian(record[end..], BinaryPrimitives.ReadUInt16LittleEndian(record[(arrayOffset + i * 2)..]));
        }
        return true;
    }

    private struct Record
    {
        public long Parent;
        public long Size;
        public long LastWrite;
        public int NameOffset;
        public ushort NameLength;
        public bool IsDirectory;
        public bool IsListed;
        public FileAttributes Attributes;
    }

    private readonly record struct Extent(long Offset, long Length);
    private readonly record struct Extension(long BaseRecord, long Size);
    private readonly record struct Directory(long Record, int Index);
}
