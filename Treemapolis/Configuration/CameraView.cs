namespace Treemapolis.Configuration;

// where the camera looked from when the window closed, the map of a folder is always laid out on the same square so it lands in the same place.
public sealed class CameraView
{
    public float TargetX { get; set; }
    public float TargetY { get; set; }
    public float TargetZ { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float Distance { get; set; }
}
