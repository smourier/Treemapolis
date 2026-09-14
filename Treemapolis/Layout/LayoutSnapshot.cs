namespace Treemapolis.Layout;

public sealed class LayoutSnapshot
{
    private const int _floorCells = 256;
    private const InstanceFlags _floorFlags = InstanceFlags.Visible | InstanceFlags.Container;
    private const InstanceFlags _notFloorFlags = InstanceFlags.Collapsed | InstanceFlags.Aggregate;

    private float[]? _floor;

    public required NamespaceTree Tree { get; init; }
    public required int Root { get; init; }
    public required TargetInstance[] Instances { get; init; }
    public required int Count { get; init; }
    public required int TreeVersion { get; init; }
    public required int SettingsVersion { get; init; }
    public Vector3 BoundsMin { get; init; }
    public Vector3 BoundsMax { get; init; }
    public int Containers { get; init; }
    public int Files { get; init; }
    public int Aggregates { get; init; }
    public double BuildMilliseconds { get; init; }

    // the displayed containers in breadth first order, each with its range of files in FileEntries.
    public int[] ContainerEntries { get; init; } = [];
    public int[] ContainerFileStarts { get; init; } = [];
    public int[] ContainerFileCounts { get; init; } = [];
    public int[] FileEntries { get; init; } = [];

    // keyed by the entry that draws the aggregate block, the largest of the items it stands for.
    public IReadOnlyDictionary<int, AggregateInfo> AggregateInfos { get; init; } = new Dictionary<int, AggregateInfo>();

    // the top of the folders under a point, what the camera stands on when it comes down, the buildings on them left out.
    // it is read from a coarse grid made the first time it is asked for, a cell only counts a folder that covers it whole.
    public float GetFloorHeight(float x, float z)
    {
        var floor = LazyInitializer.EnsureInitialized(ref _floor, BuildFloor);
        var extent = BoundsMax - BoundsMin;
        if (extent.X <= 0 || extent.Z <= 0)
            return 0;

        var cellX = (int)MathF.Floor((x - BoundsMin.X) / extent.X * _floorCells);
        var cellZ = (int)MathF.Floor((z - BoundsMin.Z) / extent.Z * _floorCells);
        if (cellX < 0 || cellZ < 0 || cellX >= _floorCells || cellZ >= _floorCells)
            return 0;

        return floor[cellZ * _floorCells + cellX];
    }

    private float[] BuildFloor()
    {
        var floor = new float[_floorCells * _floorCells];
        var extent = BoundsMax - BoundsMin;
        if (extent.X <= 0 || extent.Z <= 0)
            return floor;

        var scaleX = _floorCells / extent.X;
        var scaleZ = _floorCells / extent.Z;
        foreach (var entry in ContainerEntries)
        {
            ref readonly var instance = ref Instances[entry];
            if ((instance.Flags & _floorFlags) != _floorFlags || (instance.Flags & _notFloorFlags) != 0)
                continue;

            var top = instance.Position.Y + instance.Size.Y;
            var firstX = Math.Max(0, (int)MathF.Ceiling((instance.Position.X - instance.Size.X * 0.5f - BoundsMin.X) * scaleX));
            var lastX = Math.Min(_floorCells, (int)MathF.Floor((instance.Position.X + instance.Size.X * 0.5f - BoundsMin.X) * scaleX));
            var firstZ = Math.Max(0, (int)MathF.Ceiling((instance.Position.Z - instance.Size.Z * 0.5f - BoundsMin.Z) * scaleZ));
            var lastZ = Math.Min(_floorCells, (int)MathF.Floor((instance.Position.Z + instance.Size.Z * 0.5f - BoundsMin.Z) * scaleZ));
            for (var cellZ = firstZ; cellZ < lastZ; cellZ++)
            {
                var row = floor.AsSpan(cellZ * _floorCells + firstX, Math.Max(0, lastX - firstX));
                foreach (ref var cell in row)
                {
                    cell = MathF.Max(cell, top);
                }
            }
        }
        return floor;
    }

    public bool IsDisplayed(int entry) => entry >= 0 && entry < Count && (Instances[entry].Flags & InstanceFlags.Visible) != 0;
}
