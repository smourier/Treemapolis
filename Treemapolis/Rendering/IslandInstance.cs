namespace Treemapolis.Rendering;

// mirrors IslandInstance in Shaders\Island.hlsli, field for field.
[StructLayout(LayoutKind.Sequential)]
public struct IslandInstance
{
    public Vector3 Position;
    public uint Color;
    public Vector3 Size;
    public float Lift;
    public float Glow;
    public float Selected;
}
