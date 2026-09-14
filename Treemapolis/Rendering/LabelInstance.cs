namespace Treemapolis.Rendering;

// mirrors LabelInstance in Shaders\Labels.hlsli, field for field.
[StructLayout(LayoutKind.Sequential)]
public struct LabelInstance
{
    public const uint ContainerKind = 0;
    public const uint FileKind = 1;

    public int Entry;
    public uint Kind;
    public Vector2 UvOffset;
    public Vector2 UvSize;
    public Vector2 WorldSize;
    public float FrontInset;
    public float Padding;
}
