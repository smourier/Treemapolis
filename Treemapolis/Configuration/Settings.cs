namespace Treemapolis.Configuration;

public sealed class Settings
{
    public Appearance Appearance { get; set; }

    // what the window is made of, it needs Windows 11 and stays off until asked for.
    public Material Material { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter<ColorMode>))]
    public ColorMode ColorMode { get; set; }

    // samples per pixel of the scene, one is no antialiasing.
    public int Antialias { get; set; } = 4;

    public bool VSync { get; set; } = true;
    public bool ShowStatus { get; set; } = true;
    public bool ShowPerformance { get; set; } = true;
    public bool ShowLegend { get; set; } = true;
    public bool ShowLabels { get; set; } = true;
    public bool ShowThumbnails { get; set; } = true;
    public bool ShowShadows { get; set; } = true;
    public ScreenEffect Effect { get; set; }

    // how large the top of an image block must be on screen before it shows its picture.
    public int MinimumThumbnailPixels { get; set; } = DefaultMinimumThumbnailPixels;

    public const int DefaultMinimumThumbnailPixels = 40;

    public Ground Ground { get; set; }

    // hidden and super hidden items are left off the map unless this is on.
    public bool ShowHidden { get; set; }

    // the island of drives and places on the left of the window.
    public bool ShowIsland { get; set; } = true;

    // what changes on disk, and drives coming and going, show on the map as they happen.
    public bool WatchChanges { get; set; } = true;

    // the height of the buildings as a percentage of their natural height.
    public double Elevation { get; set; } = DefaultElevation;

    public const double DefaultElevation = 100;

    // where the sun's light goes on the ground in degrees, zero towards the default camera, a little aside so the fronts are not all in shade.
    public double SunAngle { get; set; } = DefaultSunAngle;

    public const double DefaultSunAngle = 35;

    // where the map and the camera were when the window closed, reopened when no location is given.
    public NavigationLocation? LastLocation { get; set; }
    public CameraView? Camera { get; set; }

    // where the window was, written by WindowPosition as one line.
    public string Window { get; set; } = string.Empty;

    public List<RecentFolder> RecentFolders { get; set; } = [];
}
