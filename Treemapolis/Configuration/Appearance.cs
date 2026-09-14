namespace Treemapolis.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter<Appearance>))]
public enum Appearance
{
    System,
    Dark,
    Light,
}
