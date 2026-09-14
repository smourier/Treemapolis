namespace Treemapolis.Namespace;

// winioctl.h, only the part that FSCTL_GET_NTFS_VOLUME_DATA always returns, the extended data that follows it is not needed.
[StructLayout(LayoutKind.Sequential)]
#pragma warning disable CA1707 // Identifiers should not contain underscores
public struct NTFS_VOLUME_DATA_BUFFER
#pragma warning restore CA1707
{
    public long VolumeSerialNumber;
    public long NumberSectors;
    public long TotalClusters;
    public long FreeClusters;
    public long TotalReserved;
    public uint BytesPerSector;
    public uint BytesPerCluster;
    public uint BytesPerFileRecordSegment;
    public uint ClustersPerFileRecordSegment;
    public long MftValidDataLength;
    public long MftStartLcn;
    public long Mft2StartLcn;
    public long MftZoneStart;
    public long MftZoneEnd;
}
