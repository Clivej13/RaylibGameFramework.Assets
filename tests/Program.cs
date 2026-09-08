using Raylib_cs;
using RaylibGameFramework.Assets;

// Native integration tests: a graphics context is required.
public class ModelInstanceTests
{
[Xunit.Theory]
[Xunit.InlineData("triangle.obj", false)]
[Xunit.InlineData("skinned.gltf", true)]
public unsafe void StaticAndSkinnedModelsLoad(string path, bool skinned)
{
    var assets = new AssetManager(new AssetConfig { Assets = [
        new() { Key = "Model", Type = "Model", Path = path },
        new() { Key = "Animation", Type = "ModelAnimations", Path = path }
    ] });
    Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
    Raylib.InitWindow(64, 64, "Model validation integration tests");
    try
    {
        Check(Raylib.IsWindowReady(), "Graphics context");
        Model raw = Raylib.LoadModel(Path.Combine(AppContext.BaseDirectory, path));
        try
        {
            Check(raw.MeshCount > 0 && raw.Meshes != null, "Native mesh loaded");
            if (skinned)
            {
                Check(raw.Skeleton.BoneCount > 0 && raw.Skeleton.Bones != null && raw.Skeleton.BindPose != null, "Native skeleton loaded");
                Check(raw.Meshes[0].BoneIndices != null && raw.Meshes[0].BoneWeights != null, "Skin weights loaded");
                Console.WriteLine($"Native IsModelValid={Raylib.IsModelValid(raw)}, bone VBOs={raw.Meshes[0].VboId[7]},{raw.Meshes[0].VboId[8]}");
            }
        }
        finally { Raylib.UnloadModel(raw); }

        assets.RequireAsset("Model");
        Check(assets.ProcessNext(), "Shared model accepted");
        var instance = assets.CreateModelInstance("Model");
        Check(instance.Model.Meshes != assets.GetModel("Model").Meshes, "Independent model accepted");
        if (skinned)
        {
            assets.RequireAsset("Animation");
            Check(assets.ProcessNext(), "Animation loaded");
            var clips = assets.GetModelAnimations("Animation");
            Check(clips.Length > 0 && clips[0].KeyFrameCount > 1, "Animated fixture has frames");
            Check(Raylib.IsModelAnimationValid(instance.Model, clips[0]), "Compatible skeleton");
            Raylib.UpdateModelAnimation(instance.Model, clips[0], clips[0].KeyFrameCount - 1);
            Check(instance.Model.Meshes[0].AnimVertices != null, "Animated vertex storage exists");
            Check(instance.Model.Meshes[0].AnimVertices[1] > 0.5f, "Animation moves the mesh");
        }
        assets.ReleaseModelInstance(instance);
        Check(assets.IsLoaded("Model"), "Shared model remains resident");
    }
    finally
    {
        assets.UnloadAll();
        Raylib.CloseWindow();
    }
}

[Xunit.Fact]
public void EmptyModelIsRejectedWithoutChangingResidency()
{
    var assets = new AssetManager(new AssetConfig { Assets = [
        new() { Key = "Empty", Type = "Model", Path = "empty.gltf" }
    ] });
    Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
    Raylib.InitWindow(64, 64, "Invalid model cleanup tests");
    try
    {
        Check(Raylib.IsWindowReady(), "Graphics context");
        assets.RequireAsset("Empty");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var sharedError = Xunit.Assert.Throws<InvalidOperationException>(() => assets.ProcessNext());
            Xunit.Assert.IsType<InvalidDataException>(sharedError.InnerException);
            var instanceError = Xunit.Assert.Throws<InvalidOperationException>(() => assets.CreateModelInstance("Empty"));
            Xunit.Assert.IsType<InvalidDataException>(instanceError.InnerException);
            Check(assets.LoadedCount == 0 && assets.PendingLoadCount == 1 &&
                assets.CompletedWorkCount == 0, "Failed loads remain pending");
        }
        assets.UnloadAll();
        assets.UnloadAll();
    }
    finally
    {
        assets.UnloadAll();
        Raylib.CloseWindow();
    }
}

[Xunit.Fact]
public unsafe void NativeInstanceLifetimes()
{
    var config = new AssetConfig
    {
        Assets = [
            new() { Key = "Model", Type = "Model", Path = "triangle.obj" },
            new() { Key = "Texture", Type = "Texture", Path = "triangle.obj" },
            new() { Key = "Font", Type = "Font", Path = "triangle.obj", Size = 16 },
            new() { Key = "Animations", Type = "ModelAnimations", Path = "triangle.obj" }
        ]
    };
    var assets = new AssetManager(config);
    var other = new AssetManager(config);
    int passed = 0;
    Raylib.SetConfigFlags(ConfigFlags.HiddenWindow);
    Raylib.InitWindow(64, 64, "Model instance integration tests");
    try
    {
        Check(Raylib.IsWindowReady(), "Graphics context");
        Test("invalid and non-model keys", () =>
        {
            Throws<ArgumentNullException>(() => assets.CreateModelInstance(null!));
            Throws<ArgumentException>(() => assets.CreateModelInstance(" "));
            Throws<KeyNotFoundException>(() => assets.CreateModelInstance("Missing"));
            foreach (string key in new[] { "Texture", "Font", "Animations" })
                Throws<ArgumentException>(() => assets.CreateModelInstance(key));
            Throws<ArgumentNullException>(() => assets.ReleaseModelInstance(null!));
        });
        Test("two instances have independent native allocations", () =>
        {
            var first = assets.CreateModelInstance("Model");
            var second = assets.CreateModelInstance("Model");
            Check(!ReferenceEquals(first, second), "Distinct wrappers");
            Check(first.Model.Meshes != second.Model.Meshes, "Distinct meshes");
            Check(first.Model.Materials != second.Model.Materials, "Distinct materials");
            Check(first.Model.Meshes[0].Vertices != second.Model.Meshes[0].Vertices, "Distinct vertex storage");
            float original = second.Model.Meshes[0].Vertices[0];
            first.Model.Meshes[0].Vertices[0] = original + 10;
            Check(second.Model.Meshes[0].Vertices[0] == original, "Mutation is isolated");
            Check(assets.LoadedCount == 0 && assets.RequiredCount == 0 && !assets.HasPendingWork,
                "Instances do not alter residency");
            assets.ReleaseModelInstance(first);
            Check(Raylib.IsModelValid(second.Model), "Other instance remains live");
            Check(second.Model.Meshes[0].Vertices[0] == original, "Other storage remains usable");
            Throws<ObjectDisposedException>(() => { _ = first.Model; });
            Throws<InvalidOperationException>(() => assets.ReleaseModelInstance(first));
            Throws<InvalidOperationException>(() => other.ReleaseModelInstance(second));
            Check(Raylib.IsModelValid(second.Model), "Foreign release does not invalidate");
            assets.ReleaseModelInstance(second);
        });
        Test("shared residency and instance lifetimes are independent", () =>
        {
            assets.RequireAsset("Model");
            Check(assets.ProcessNext(), "Shared load completes");
            var instance = assets.CreateModelInstance("Model");
            Check(instance.Model.Meshes != assets.GetModel("Model").Meshes, "Shared allocation is distinct");
            assets.ReleaseAsset("Model");
            Check(assets.ProcessNext(), "Shared unload completes");
            Check(Raylib.IsModelValid(instance.Model), "Instance survives shared unload");
            assets.RequireAsset("Model");
            assets.ProcessNext();
            assets.ReleaseModelInstance(instance);
            Check(Raylib.IsModelValid(assets.GetModel("Model")), "Shared model survives instance release");
        });
        Test("UnloadAll cleans unreleased instances and is repeatable", () =>
        {
            var first = assets.CreateModelInstance("Model");
            var second = assets.CreateModelInstance("Model");
            assets.UnloadAll();
            Throws<ObjectDisposedException>(() => { _ = first.Model; });
            Throws<ObjectDisposedException>(() => { _ = second.Model; });
            Throws<InvalidOperationException>(() => assets.ReleaseModelInstance(first));
            Throws<InvalidOperationException>(() => assets.ReleaseModelInstance(second));
            Check(assets.LoadedCount == 0 && assets.RequiredCount == 0 && !assets.HasPendingWork,
                "Catalogue cleared");
            assets.UnloadAll();
            var next = assets.CreateModelInstance("Model");
            Check(Raylib.IsModelValid(next.Model), "Manager is reusable");
            assets.ReleaseModelInstance(next);
        });
        Console.WriteLine($"PASS: {passed} integration test groups");
    }
    finally
    {
        assets.UnloadAll();
        other.UnloadAll();
        Raylib.CloseWindow();
    }

    void Test(string name, Action action)
    {
        action();
        passed++;
        Console.WriteLine($"PASS: {name}");
    }
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}

}
