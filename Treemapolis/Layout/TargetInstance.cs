namespace Treemapolis.Layout;

// where an entry should be, indexed by entry, mirrors TargetInstance in Shaders\Scene.hlsli field for field.
[StructLayout(LayoutKind.Sequential)]
public struct TargetInstance
{
    public Vector3 Position;
    public uint Color;
    public Vector3 Size;
    public InstanceFlags Flags;
    public int Parent;

    // the kind of a recent change in the top two bits, when it happened in milliseconds of the frame clock in the others.
    public uint Change;
}
