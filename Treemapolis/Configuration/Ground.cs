namespace Treemapolis.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter<Ground>))]
public enum Ground
{
    Grid,
    Plain,
    None,
}
