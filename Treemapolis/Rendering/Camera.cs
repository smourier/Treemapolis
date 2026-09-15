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
    private const int _framingSteps = 32;
    private const int _boxCorners = 8;
    private const int _centeringPasses = 4;
    private const float _minimumFramingSine = 0.2f;
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

    public float Pitch { get => _pitch; set => _pitch = Math.Clamp(value, _minimumPitch, _maximumPitch); }
    public float FieldOfView { get; set; } = MathF.PI / 3;

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

    public void Frame(Vector3 boundsMin, Vector3 boundsMax, float aspectRatio, Vector4 freeArea)
    {
        Yaw = 0;
        Pitch = _framingPitch;
        GetFraming(boundsMin, boundsMax, aspectRatio, freeArea, out var target, out var distance);
        Target = target;
        Distance = distance;
    }

    public void FlyToFrame(Vector3 boundsMin, Vector3 boundsMax, float aspectRatio, Vector4 freeArea)
    {
        GetFraming(boundsMin, boundsMax, aspectRatio, freeArea, out var target, out var distance);
        FlyTo(target, distance);
    }

    private void GetFraming(Vector3 boundsMin, Vector3 boundsMax, float aspectRatio, Vector4 freeArea, out Vector3 target, out float distance)
    {
        Span<Vector3> corners = stackalloc Vector3[_boxCorners];
        for (var i = 0; i < _boxCorners; i++)
        {
            corners[i] = new Vector3((i & 1) != 0 ? boundsMax.X : boundsMin.X, (i & 2) != 0 ? boundsMax.Y : boundsMin.Y, (i & 4) != 0 ? boundsMax.Z : boundsMin.Z);
        }

        var orbit = MathF.Max(Pitch, 0);
        var direction = new Vector3(MathF.Sin(Yaw) * MathF.Cos(orbit), MathF.Sin(orbit), MathF.Cos(Yaw) * MathF.Cos(orbit));
        var right = new Vector3(MathF.Cos(Yaw), 0, -MathF.Sin(Yaw));
        var forward = new Vector3(-MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));
        var projection = GetProjection(aspectRatio);
        var halfHeight = MathF.Tan(FieldOfView * 0.5f);
        var freeCenter = new Vector2(freeArea.X + freeArea.Z, freeArea.Y + freeArea.W) * 0.5f;
        target = new Vector3((boundsMin.X + boundsMax.X) * 0.5f, 0, (boundsMin.Z + boundsMax.Z) * 0.5f);
        distance = _maximumDistance;
        for (var pass = 0; pass < _centeringPasses; pass++)
        {
            var near = MathF.Log(_minimumDistance);
            var far = MathF.Log(_maximumDistance);
            for (var step = 0; step < _framingSteps; step++)
            {
                var middle = (near + far) * 0.5f;
                if (Measure(corners, GetViewProjection(target, direction, MathF.Exp(middle), projection), out var box) && box.X >= freeArea.X && box.Y >= freeArea.Y && box.Z <= freeArea.Z && box.W <= freeArea.W)
                {
                    far = middle;
                }
                else
                {
                    near = middle;
                }
            }

            distance = MathF.Exp(far);
            if (!Measure(corners, GetViewProjection(target, direction, distance, projection), out var fitted))
                break;

            // one unit of the view spans this much of the world at the target, sideways as it is, up the screen stretched over the ground.
            var offset = freeCenter - new Vector2(fitted.X + fitted.Z, fitted.Y + fitted.W) * 0.5f;
            var sideways = offset.X * distance * halfHeight * aspectRatio;
            var along = offset.Y * distance * halfHeight / MathF.Max(MathF.Sin(orbit), _minimumFramingSine);
            target -= right * sideways + forward * along;
        }
    }

    private static Matrix4x4 GetViewProjection(Vector3 target, Vector3 direction, float distance, Matrix4x4 projection) =>
        Matrix4x4.CreateLookAt(target + direction * distance, target, Vector3.UnitY) * projection;

    // the rectangle the corners cover on screen in normalized device coordinates, false when one is behind the eye.
    private static bool Measure(ReadOnlySpan<Vector3> corners, Matrix4x4 viewProjection, out Vector4 box)
    {
        box = new Vector4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
        foreach (var corner in corners)
        {
            var clip = Vector4.Transform(new Vector4(corner, 1), viewProjection);
            if (clip.W <= _nearPlane)
                return false;

            var x = clip.X / clip.W;
            var y = clip.Y / clip.W;
            box = new Vector4(MathF.Min(box.X, x), MathF.Min(box.Y, y), MathF.Max(box.Z, x), MathF.Max(box.W, y));
        }
        return true;
    }
}
