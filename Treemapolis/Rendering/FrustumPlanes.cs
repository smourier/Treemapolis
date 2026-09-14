namespace Treemapolis.Rendering;

// left, right, bottom, top and near. the projection has no far plane, so there is none to test.
[InlineArray(5)]
public struct FrustumPlanes
{
    private Vector4 _plane;

    public static FrustumPlanes FromViewProjection(in Matrix4x4 m)
    {
        // Gribb and Hartmann, on a row vector matrix the planes are sums and differences of its columns.
        var planes = new FrustumPlanes();
        planes[0] = Normalize(new Vector4(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41));
        planes[1] = Normalize(new Vector4(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41));
        planes[2] = Normalize(new Vector4(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42));
        planes[3] = Normalize(new Vector4(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42));

        // with depth reversed the near plane is where z reaches w.
        planes[4] = Normalize(new Vector4(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43));
        return planes;
    }

    private static Vector4 Normalize(Vector4 plane) => plane / new Vector3(plane.X, plane.Y, plane.Z).Length();
}
