namespace RaylibGameFramework.Assets;

public sealed class AssetConfig
{
    public List<AssetDefinition> Assets { get; init; } = [];

    internal void Validate()
    {
        if (Assets is null)
            throw new InvalidDataException("Assets must be an array.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in Assets)
        {
            if (asset is null || string.IsNullOrWhiteSpace(asset.Key))
                throw new InvalidDataException("Every asset must have a Key.");
            if (asset.Key != asset.Key.Trim())
                throw new InvalidDataException($"Asset key '{asset.Key}' must not have surrounding whitespace.");
            if (!keys.Add(asset.Key))
                throw new InvalidDataException($"Duplicate asset key '{asset.Key}'.");
            if (asset.Type is not ("Texture" or "Font" or "Model" or "ModelAnimations"))
                throw new InvalidDataException($"Asset '{asset.Key}' requires Type 'Texture', 'Font', 'Model' or 'ModelAnimations'.");
            if (string.IsNullOrWhiteSpace(asset.Path))
                throw new InvalidDataException($"Asset '{asset.Key}' requires a Path.");
            if (asset.Type == "Font" && asset.Size is not > 0)
                throw new InvalidDataException($"Font '{asset.Key}' requires a positive Size.");
            if (asset.Type != "Font" && asset.Size is not null)
                throw new InvalidDataException($"{asset.Type} '{asset.Key}' must not specify a font Size.");
            string resolvedPath;
            try
            {
                resolvedPath = System.IO.Path.GetFullPath(asset.Path, AppContext.BaseDirectory);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
                throw new InvalidDataException($"Asset '{asset.Key}' has an invalid Path '{asset.Path}'.", ex);
            }
            if (!File.Exists(resolvedPath))
                throw new InvalidDataException($"Asset '{asset.Key}' file does not exist: '{resolvedPath}'.");
        }
    }
}
