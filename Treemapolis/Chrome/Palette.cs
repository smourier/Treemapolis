namespace Treemapolis.Chrome;

// one complete look, written out in full for both themes, a light window is not a dark one with its values turned around.
public sealed class Palette
{
    public required D3DCOLORVALUE CaptionBackground { get; init; }
    public required D3DCOLORVALUE Text { get; init; }
    public required D3DCOLORVALUE DimText { get; init; }
    public required D3DCOLORVALUE DisabledText { get; init; }
    public required D3DCOLORVALUE Hover { get; init; }
    public required D3DCOLORVALUE Line { get; init; }
    public required D3DCOLORVALUE Selection { get; init; }
    public required D3DCOLORVALUE Accent { get; init; }
    public required D3DCOLORVALUE PanelBackground { get; init; }
    public required D3DCOLORVALUE PanelText { get; init; }
    public required D3DCOLORVALUE MenuBackground { get; init; }
    public required D3DCOLORVALUE MenuShadow { get; init; }
    public required D3DCOLORVALUE Good { get; init; }
    public required D3DCOLORVALUE Bad { get; init; }

    // the caption keeps some of itself over a material, a light material is mostly what is behind the window.
    public required D3DCOLORVALUE CaptionOnMaterial { get; init; }

    // linear, the scene renders into an sRGB target.
    public required Vector3 Sky { get; init; }

    // a light sky washes a distant map out much sooner than a dark one hides it.
    public required float Fog { get; init; }

    public static Palette Dark { get; } = new()
    {
        CaptionBackground = new(0xFF202020U),
        Text = new(0xFFE4E4E4U),
        DimText = new(0xFF8C8C8CU),
        DisabledText = new(0xFF5A5A5AU),
        Hover = new(0x22FFFFFFU),
        Line = new(0xFF323232U),
        Selection = new(0xFF0A5A96U),
        Accent = new(0xFF3E8FD0U),
        PanelBackground = new(0xC80D1117U),
        PanelText = new(0xFFD8E6F0U),
        MenuBackground = new(0xFF252526U),
        MenuShadow = new(0xE0101010U),
        Good = new(0xFF6FCF6FU),
        Bad = new(0xFFE06C6CU),
        CaptionOnMaterial = new(0x00000000U),
        Sky = new(0.01f, 0.015f, 0.04f),
        Fog = 1,
    };

    public static Palette Light { get; } = new()
    {
        CaptionBackground = new(0xFFF3F3F3U),
        Text = new(0xFF1A1A1AU),
        DimText = new(0xFF6B6B6BU),
        DisabledText = new(0xFFA0A0A0U),
        Hover = new(0x1A000000U),
        Line = new(0xFFE2E2E2U),
        Selection = new(0xFFCCE8FFU),
        Accent = new(0xFF0A5A96U),
        PanelBackground = new(0xE6F9F9F9U),
        PanelText = new(0xFF1F1F1FU),
        MenuBackground = new(0xFFF9F9F9U),
        MenuShadow = new(0x40000000U),
        Good = new(0xFF1E8E3EU),
        Bad = new(0xFFC5221FU),
        CaptionOnMaterial = new(0xB3FFFFFFU),
        Sky = new(0.45f, 0.55f, 0.68f),
        Fog = 0.4f,
    };
}
