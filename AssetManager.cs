using Raylib_cs;

namespace RaylibGameFramework.Assets;

/// <summary>Owns native assets. Use on the graphics thread and unload before closing the window.</summary>
public sealed class AssetManager
{
    private readonly Dictionary<string, AssetDefinition> _catalogue;
    private readonly HashSet<string> _required = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Font> _fonts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Model> _models = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OwnedModelAnimations> _modelAnimations = new(StringComparer.Ordinal);

    private readonly HashSet<ModelInstance> _modelInstances = new(ReferenceEqualityComparer.Instance);

    private sealed unsafe class OwnedModelAnimations
    {
        private readonly ModelAnimation* _animations;
        private readonly int _count;

        public OwnedModelAnimations(string path)
        {
            int count = 0;
            ModelAnimation* animations = Raylib.LoadModelAnimations(path, ref count);
            if (animations == null || count <= 0)
            {
                if (animations != null)
                    Raylib.UnloadModelAnimations(animations, count);
                throw new InvalidDataException("Raylib could not load any model animations.");
            }
            _animations = animations;
            _count = count;
        }

        public ReadOnlySpan<ModelAnimation> Borrow() => new(_animations, _count);
        public void Unload() => Raylib.UnloadModelAnimations(_animations, _count);
    }

    public AssetManager(AssetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        _catalogue = config.Assets.ToDictionary(asset => asset.Key,
            asset => asset with { Path = System.IO.Path.GetFullPath(asset.Path, AppContext.BaseDirectory) },
            StringComparer.Ordinal);
    }

    public int LoadedCount => _textures.Count + _fonts.Count + _models.Count + _modelAnimations.Count;

    public int TotalCount => _catalogue.Count;
    public int RequiredCount => _required.Count;
    public int PendingLoadCount => _required.Count(key => !IsLoaded(key));
    public int PendingUnloadCount => _textures.Keys.Concat(_fonts.Keys).Concat(_models.Keys).Concat(_modelAnimations.Keys).Count(key => !_required.Contains(key));
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
            else if (asset.Type == "Font")
                _fonts.Add(asset.Key, LoadFont(path, asset.Size!.Value));
            else if (asset.Type == "Model")
                _models.Add(asset.Key, LoadModel(path));
            else
                _modelAnimations.Add(asset.Key, new OwnedModelAnimations(path));

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

    private static Model LoadModel(string path)
    {
        Model model = Raylib.LoadModel(path);
        if (!HasUsableModelData(model))
        {
            Raylib.UnloadModel(model);
            throw new InvalidDataException("Raylib could not load a usable model.");
        }
        return model;
    }

    // Raylib 6.0 IsModelValid requires bone VBOs even when GPU skinning is disabled.
    // CPU skinning uses BoneIndices/BoneWeights without uploading those attributes.
    // Validate model/mesh data independently of optional GPU attribute buffers.
    private static unsafe bool HasUsableModelData(Model model)
    {
        if (model.MeshCount <= 0 || model.Meshes == null ||
            model.MaterialCount <= 0 || model.Materials == null || model.MeshMaterial == null)
            return false;

        for (int i = 0; i < model.MeshCount; i++)
        {
            Mesh mesh = model.Meshes[i];
            if (mesh.VertexCount <= 0 || mesh.TriangleCount <= 0 || mesh.Vertices == null ||
                model.MeshMaterial[i] < 0 || model.MeshMaterial[i] >= model.MaterialCount)
                return false;

            // Unindexed triangles need three vertices each; indexed ones must stay in range.
            if (mesh.Indices == null)
            {
                if ((long)mesh.TriangleCount * 3 > mesh.VertexCount)
                    return false;
            }
            else
            {
                for (long index = 0; index < (long)mesh.TriangleCount * 3; index++)
                    if (mesh.Indices[index] >= mesh.VertexCount)
                        return false;
            }
        }
        return true;
    }

    /// <summary>Returned handles are borrowed; only this manager should unload them.</summary>
    public Texture2D GetTexture(string key) => _textures.TryGetValue(key, out var texture)
        ? texture : throw new KeyNotFoundException($"Texture '{key}' is not loaded.");

    /// <summary>Returned handles are borrowed; only this manager should unload them.</summary>
    public Font GetFont(string key) => _fonts.TryGetValue(key, out var font)
        ? font : throw new KeyNotFoundException($"Font '{key}' is not loaded.");

    /// <summary>Returns a borrowed model, valid until this manager unloads it. Callers must not unload it or its resources.</summary>
    public Model GetModel(string key) => _models.TryGetValue(key, out var model)
        ? model : throw new KeyNotFoundException($"Model '{key}' is not loaded.");

    /// <summary>Immediately loads an independent model from a Model catalogue entry.
    /// Does not change catalogue residency or requirements. Use on the graphics thread.</summary>
    public ModelInstance CreateModelInstance(string modelKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);
        if (!_catalogue.TryGetValue(modelKey, out var asset))
            throw new KeyNotFoundException($"Asset '{modelKey}' is not in the catalogue.");
        if (asset.Type != "Model")
            throw new ArgumentException($"Asset '{modelKey}' is not a Model asset.", nameof(modelKey));

        try
        {
            if (!File.Exists(asset.Path))
                throw new FileNotFoundException("Asset file does not exist.", asset.Path);

            Model model = LoadModel(asset.Path);
            try
            {
                var instance = new ModelInstance(model);
                _modelInstances.Add(instance);
                return instance;
            }
            catch
            {
                Raylib.UnloadModel(model);
                throw;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to create Model instance '{modelKey}' from '{asset.Path}': {ex.Message}", ex);
        }
    }

    /// <summary>Unloads an instance owned by this manager exactly once.
    /// Null, foreign and previously released instances are rejected.</summary>
    public void ReleaseModelInstance(ModelInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!_modelInstances.Remove(instance))
            throw new InvalidOperationException("Model instance is not live in this manager (already released or owned by another manager).");

        Model model = instance.Model;
        instance.Invalidate();
        Raylib.UnloadModel(model);
    }

    /// <summary>Returns borrowed animations, valid until this manager unloads the asset.
    /// Callers must not unload the animations or their resources, or retain them after unloading.
    /// One animation collection counts as one loaded asset, regardless of clip count.</summary>
    public ReadOnlySpan<ModelAnimation> GetModelAnimations(string key) => _modelAnimations.TryGetValue(key, out var animations)
        ? animations.Borrow() : throw new KeyNotFoundException($"Model animations '{key}' are not loaded.");

    public bool IsLoaded(string key) => _textures.ContainsKey(key) || _fonts.ContainsKey(key)
        || _models.ContainsKey(key) || _modelAnimations.ContainsKey(key);

    private void UnloadOwned(string key)
    {
        if (_textures.Remove(key, out var texture))
            Raylib.UnloadTexture(texture);
        else if (_fonts.Remove(key, out var font))
            Raylib.UnloadFont(font);
        else if (_models.Remove(key, out var model))
            Raylib.UnloadModel(model);
        else if (_modelAnimations.Remove(key, out var animations))
            animations.Unload();
        else
            return;
        Console.WriteLine($"[Assets] UNLOAD {key}");
    }

    /// <summary>Immediately unloads all owned resources and clears requirements. Safe to call again.</summary>
    public void UnloadAll()
    {
        foreach (var instance in _modelInstances.ToArray())
            ReleaseModelInstance(instance);
        foreach (string key in _textures.Keys.Concat(_fonts.Keys).Concat(_models.Keys).Concat(_modelAnimations.Keys).ToArray())
            UnloadOwned(key);
        _required.Clear();
        ResetTransitionProgress();
    }
}
