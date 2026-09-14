namespace Treemapolis.Rendering;

public sealed class Camera
{
    private const float _nearPlane = 0.05f;
    private const float _orbitSpeed = 0.006f;
    private const float _minimumPitch = -MathF.PI / 3;
    private const float _eyeHeight = 0.3f;
    private const float _maximumPitch = MathF.PI * 0.49f;
    private const float _minimumDistance = 1;
    private const float _maximumDistance = 1e6f;
    private const float _zoomPerNotch = 0.85f;
    private const float _framingPitch = MathF.PI / 7;
    private const float _framingMargin = 0.75f;
    private const double _flightSeconds = 0.8;

    private Vector3 _flightStartTarget;
    private Vector3 _flightEndTarget;
    private float _flightStartDistance;
    private float _flightEndDistance;
    private double _flightStart = double.NaN;
    private double _flightTime;

    public Vector3 Target { get; set; }
    public float Distance { get; set; } = 30;
    public float Yaw { get; set; }
    private float _pitch = _framingPitch;

    // above zero the camera orbits down towards the ground, below zero it lies on the ground and looks up, clamped however it is set.
    public float Pitch { get => _pitch; set => _pitch = Math.Clamp(value, _minimumPitch, _maximumPitch); }
    public float FieldOfView { get; set; } = MathF.PI / 3;

    // the map the camera stands on when it comes down, its folders are the floor, null is the bare ground.
    public LayoutSnapshot? Terrain { get; set; }

    // never below the floor under it, the top of the folders there or the ground, the eye stays a little above it.
    public Vector3 Position
    {
        get
        {
            var orbit = MathF.Max(Pitch, 0);
            var cosPitch = MathF.Cos(orbit);
            var offset = new Vector3(MathF.Sin(Yaw) * cosPitch, MathF.Sin(orbit), MathF.Cos(Yaw) * cosPitch);
            var position = Target + offset * Distance;
            position.Y = MathF.Max(position.Y, (Terrain?.GetFloorHeight(position.X, position.Z) ?? 0) + _eyeHeight);
            return position;
        }
    }

    // the target, or a point above it when the camera lies on the ground and looks up, so the view tilts on from where the orbit stopped.
    public Vector3 LookPoint => Pitch >= 0 ? Target : Target + new Vector3(0, MathF.Tan(-Pitch) * Distance, 0);

    public bool IsFlying => !double.IsNaN(_flightStart);

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Position, LookPoint, Vector3.UnitY);

    // an infinite far plane with depth reversed, near maps to 1, so a whole disk fits without clipping or z fighting.
    public Matrix4x4 GetProjection(float aspectRatio)
    {
        var f = 1 / MathF.Tan(FieldOfView * 0.5f);
        return new Matrix4x4(
            f / aspectRatio, 0, 0, 0,
            0, f, 0, 0,
            0, 0, 0, -1,
            0, 0, _nearPlane, 0);
    }

    // orthographic with depth reversed like every other view of the scene, near maps to 1 and far to 0.
    public static Matrix4x4 CreateReversedOrthographic(float left, float right, float bottom, float top, float nearPlane, float farPlane)
    {
        var reverse = new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, -1, 0, 0, 0, 1, 1);
        return Matrix4x4.CreateOrthographicOffCenter(left, right, bottom, top, nearPlane, farPlane) * reverse;
    }

    // how many pixels one unit spans at a distance of one unit.
    public float GetPixelScale(float viewportHeight) => viewportHeight * 0.5f / MathF.Tan(FieldOfView * 0.5f);

    public void Orbit(float deltaX, float deltaY)
    {
        Yaw -= deltaX * _orbitSpeed;
        Pitch += deltaY * _orbitSpeed;
    }

    // the target slides on the ground plane by as much as the cursor moved over it.
    public void Pan(float deltaX, float deltaY, float viewportHeight)
    {
        var unitsPerPixel = Distance / GetPixelScale(viewportHeight);
        var right = new Vector3(MathF.Cos(Yaw), 0, -MathF.Sin(Yaw));
        var forward = new Vector3(-MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));
        Target += (-right * deltaX + forward * deltaY) * unitsPerPixel;
    }

    // glides the target and the distance, the angle the user chose is kept.
    public void FlyTo(Vector3 target, float distance)
    {
        _flightStartTarget = Target;
        _flightStartDistance = Distance;
        _flightEndTarget = target;
        _flightEndDistance = Math.Clamp(distance, _minimumDistance, _maximumDistance);
        _flightStart = _flightTime;
    }

    public void CancelFlight() => _flightStart = double.NaN;

    public void Update(double time)
    {
        _flightTime = time;
        if (!IsFlying)
            return;

        var t = (float)Math.Clamp((time - _flightStart) / _flightSeconds, 0, 1);
        var eased = t * t * (3 - 2 * t);
        Target = Vector3.Lerp(_flightStartTarget, _flightEndTarget, eased);

        // distance is eased in log space, a flight from a whole disk down to one block would otherwise rush the last part.
        Distance = MathF.Exp(float.Lerp(MathF.Log(_flightStartDistance), MathF.Log(_flightEndDistance), eased));
        if (t >= 1)
        {
            _flightStart = double.NaN;
        }
    }

    public void Zoom(float notches) => Distance = Math.Clamp(Distance * MathF.Pow(_zoomPerNotch, notches), _minimumDistance, _maximumDistance);

    // from the front, looking down on the whole map.
    public void Frame(Vector3 boundsMin, Vector3 boundsMax)
    {
        GetFraming(boundsMin, boundsMax, out var target, out var distance);
        Target = target;
        Distance = distance;
        Yaw = 0;
        Pitch = _framingPitch;
    }

    public void FlyToFrame(Vector3 boundsMin, Vector3 boundsMax)
    {
        GetFraming(boundsMin, boundsMax, out var target, out var distance);
        FlyTo(target, distance);
    }

    private static void GetFraming(Vector3 boundsMin, Vector3 boundsMax, out Vector3 target, out float distance)
    {
        var extent = boundsMax - boundsMin;
        target = new Vector3((boundsMin.X + boundsMax.X) * 0.5f, 0, (boundsMin.Z + boundsMax.Z) * 0.5f);
        distance = Math.Clamp(MathF.Max(extent.X, extent.Z) * _framingMargin + extent.Y, _minimumDistance, _maximumDistance);
    }
}
