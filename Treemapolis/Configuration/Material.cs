namespace Treemapolis.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter<Material>))]
public enum Material
{
    None,
    Mica,
    Acrylic,
}
