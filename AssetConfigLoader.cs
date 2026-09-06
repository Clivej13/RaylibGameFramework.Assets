using System.Text.Json;

namespace RaylibGameFramework.Assets;

public static class AssetConfigLoader
{
    public static AssetConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string resolvedPath = System.IO.Path.IsPathRooted(path)
            ? path
            : System.IO.Path.Combine(AppContext.BaseDirectory, path);

        try
        {
            var config = JsonSerializer.Deserialize<AssetConfig>(File.ReadAllText(resolvedPath))
                ?? throw new InvalidDataException("Asset configuration cannot be null.");
            config.Validate();
            return config;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Could not load asset configuration '{resolvedPath}': {ex.Message}", ex);
        }
    }
}
