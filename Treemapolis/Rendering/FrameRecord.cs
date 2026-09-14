namespace Treemapolis.Rendering;

// what one frame asked of the GPU, kept so a lost device can be explained by the frames that led to it.
public readonly record struct FrameRecord
{
    public long Frame { get; init; }
    public double Time { get; init; }
    public uint Slot { get; init; }
    public uint Width { get; init; }
    public uint Height { get; init; }
    public uint SampleCount { get; init; }
    public uint InstanceCount { get; init; }
    public uint Capacity { get; init; }
    public uint ChunkCount { get; init; }
    public uint LastVisible { get; init; }
    public bool Animated { get; init; }
    public bool Drawn { get; init; }
    public int LayoutUploaded { get; init; }
    public int ThumbnailsUploaded { get; init; }
    public ulong EntryCellsBytes { get; init; }
    public int IslandInstances { get; init; }
    public bool Picked { get; init; }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"#{Frame} t={Time:0.000} slot={Slot} {Width}x{Height} msaa={SampleCount} instances={InstanceCount}/{Capacity} chunks={ChunkCount} visible={LastVisible} animate={Animated} draw={Drawn} layout={LayoutUploaded} thumbs={ThumbnailsUploaded} cells={EntryCellsBytes} island={IslandInstances} pick={Picked}");
}
