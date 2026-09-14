namespace Treemapolis.Layout;

// mirrors the INSTANCE_ flags in Shaders\Common.hlsli.
[Flags]
public enum InstanceFlags : uint
{
    None = 0,
    Visible = 0x1,
    Container = 0x2,
    Hidden = 0x8,
    Drive = 0x10,
    Collapsed = 0x20,
    Aggregate = 0x40,
}
