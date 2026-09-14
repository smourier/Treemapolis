namespace Treemapolis.Configuration;

// cartoon and pixel art mirror the EFFECT_ values in Shaders\Post.hlsli, neon and New York are drawn as lines in the scene.
[JsonConverter(typeof(JsonStringEnumConverter<ScreenEffect>))]
public enum ScreenEffect
{
    None,
    Cartoon,
    PixelArt,
    Neon,
    NewYork2027,
}
