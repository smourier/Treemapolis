namespace Treemapolis.Rendering;

// mirrors FrameConstants in Shaders\Common.hlsli, field for field.
[StructLayout(LayoutKind.Sequential)]
public struct FrameConstants
{
    public Matrix4x4 ViewProjection;
    public Vector3 CameraPosition;
    public float Time;
    public Vector3 LightDirection;
    public uint InstanceCount;
    public FrustumPlanes Frustum;
    public float PixelScale;
    public float MinimumPixels;
    public float FogDensity;
    public uint HoveredEntry;
    public Vector3 CameraRight;
    public uint SelectedEntry;
    public Vector3 CameraUp;
    public uint ShowThumbnails;
    public Vector4 SkyColor;
    public Matrix4x4 LightViewProjection;
    public float ShadowTexel;
    public uint ShowShadows;
    public uint LineEffect;
    public float LinePadding;
}
