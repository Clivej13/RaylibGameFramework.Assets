namespace RaylibGameFramework.Assets;

public sealed record AssetDefinition
{
    public string Key { get; init; } = "";
    public string Type { get; init; } = "";
    public string Path { get; init; } = "";
    public int? Size { get; init; }
}
