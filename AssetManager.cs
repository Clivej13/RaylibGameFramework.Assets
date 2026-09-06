using Raylib_cs;

namespace RaylibGameFramework.Assets;

/// <summary>Owns native assets. Use on the graphics thread and unload before closing the window.</summary>
public sealed class AssetManager
{
    private readonly Dictionary<string, AssetDefinition> _catalogue;
    private readonly HashSet<string> _required = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Font> _fonts = new(StringComparer.Ordinal);

    public AssetManager(AssetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        _catalogue = config.Assets.ToDictionary(asset => asset.Key,
            asset => asset with { Path = System.IO.Path.GetFullPath(asset.Path, AppContext.BaseDirectory) },
            StringComparer.Ordinal);
    }

    public int LoadedCount => _textures.Count + _fonts.Count;

    public int TotalCount => _catalogue.Count;
    public int RequiredCount => _required.Count;
    public int PendingLoadCount => _required.Count(key => !IsLoaded(key));
    public int PendingUnloadCount => _textures.Keys.Concat(_fonts.Keys).Count(key => !_required.Contains(key));
    public bool HasPendingWork => PendingUnloadCount != 0 || PendingLoadCount != 0;

    /// <summary>Number of loads and unloads in the current transition.</summary>
    public int TotalWorkCount { get; private set; }
    public int CompletedWorkCount { get; private set; }
    public float Progress => TotalWorkCount == 0 ? 1.0f : (float)CompletedWorkCount / TotalWorkCount;

    private void ResetTransitionProgress()
    {
        TotalWorkCount = PendingLoadCount + PendingUnloadCount;
        CompletedWorkCount = 0;
    }

    // Requirements are a set, not reference counts. Validate batches before changing anything.
    public void RequireAsset(string key) => RequireAssets(key);
    public void RequireAssets(params string[] keys)
    {
        ValidateKeys(keys);
        int previousCount = _required.Count;
        _required.UnionWith(keys);
        if (_required.Count != previousCount)
            ResetTransitionProgress();
    }

    public void ReleaseAsset(string key) => ReleaseAssets(key);
    public void ReleaseAssets(params string[] keys)
    {
        ValidateKeys(keys);
        int previousCount = _required.Count;
        _required.ExceptWith(keys);
        if (_required.Count != previousCount)
            ResetTransitionProgress();
    }

    public void SetRequiredAssets(params string[] keys)
    {
        ValidateKeys(keys);
        if (_required.SetEquals(keys))
            return;

        _required.Clear();
        _required.UnionWith(keys);
        ResetTransitionProgress();
    }

    public void ClearRequiredAssets()
    {
        if (_required.Count == 0)
            return;

        _required.Clear();
        ResetTransitionProgress();
    }

    private void ValidateKeys(string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        foreach (string key in keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            if (!_catalogue.ContainsKey(key))
                throw new KeyNotFoundException($"Asset '{key}' is not in the catalogue.");
        }
    }

    /// <summary>Processes at most one owned asset, unloads first. Returns true when work is complete.
    /// Failed loads remain pending. Call only on the graphics thread with a live window.</summary>
    public bool ProcessNext()
    {
        // Deriving the delta avoids stale queue entries when requirements change between frames.
        string? unloadKey = _catalogue.Keys.FirstOrDefault(key => IsLoaded(key) && !_required.Contains(key));
        if (unloadKey is not null)
        {
            UnloadOwned(unloadKey);
            CompletedWorkCount++;
            return !HasPendingWork;
        }

        AssetDefinition? asset = _catalogue.Values.FirstOrDefault(asset =>
            _required.Contains(asset.Key) && !IsLoaded(asset.Key));
        if (asset is null)
            return true;

        string path = asset.Path;
        try
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Asset file does not exist.", path);

            if (asset.Type == "Texture")
                _textures.Add(asset.Key, LoadTexture(path));
            else
                _fonts.Add(asset.Key, LoadFont(path, asset.Size!.Value));

            CompletedWorkCount++;
            Console.WriteLine($"[Assets] LOAD {asset.Key}");
            return !HasPendingWork;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to load {asset.Type} asset '{asset.Key}' from '{path}': {ex.Message}", ex);
        }
    }

    private static Texture2D LoadTexture(string path)
    {
        Image image = Raylib.LoadImage(path);
        if (!Raylib.IsImageValid(image))
            throw new InvalidDataException("Raylib could not decode the image.");
        try
        {
            Texture2D texture = Raylib.LoadTextureFromImage(image);
            if (!Raylib.IsTextureValid(texture))
                throw new InvalidDataException("Raylib could not create the texture.");
            return texture;
        }
        finally
        {
            Raylib.UnloadImage(image);
        }
    }

    private static Font LoadFont(string path, int size)
    {
        Font font = Raylib.LoadFontEx(path, size, Array.Empty<int>(), 0);
        // Raylib can return its borrowed default font on failure. Never cache or unload it.
        if (font.Texture.Id == Raylib.GetFontDefault().Texture.Id)
            throw new InvalidDataException("Raylib returned the default font instead of the configured font.");
        if (!Raylib.IsFontValid(font))
        {
            Raylib.UnloadFont(font);
            throw new InvalidDataException("Raylib could not load the font.");
        }
        return font;
    }

    /// <summary>Returned handles are borrowed; only this manager should unload them.</summary>
    public Texture2D GetTexture(string key) => _textures.TryGetValue(key, out var texture)
        ? texture : throw new KeyNotFoundException($"Texture '{key}' is not loaded.");

    /// <summary>Returned handles are borrowed; only this manager should unload them.</summary>
    public Font GetFont(string key) => _fonts.TryGetValue(key, out var font)
        ? font : throw new KeyNotFoundException($"Font '{key}' is not loaded.");

    public bool IsLoaded(string key) => _textures.ContainsKey(key) || _fonts.ContainsKey(key);

    private void UnloadOwned(string key)
    {
        if (_textures.Remove(key, out var texture))
            Raylib.UnloadTexture(texture);
        else if (_fonts.Remove(key, out var font))
            Raylib.UnloadFont(font);
        else
            return;
        Console.WriteLine($"[Assets] UNLOAD {key}");
    }

    /// <summary>Immediately unloads all owned resources and clears requirements. Safe to call again.</summary>
    public void UnloadAll()
    {
        foreach (string key in _textures.Keys.Concat(_fonts.Keys).ToArray())
            UnloadOwned(key);
        _required.Clear();
        ResetTransitionProgress();
    }
}
