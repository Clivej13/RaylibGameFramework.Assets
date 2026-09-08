using Raylib_cs;

namespace RaylibGameFramework.Assets;

/// <summary>An independently loaded model owned by the creating AssetManager.
/// Release through that manager, on the graphics thread, before closing the window.</summary>
public sealed class ModelInstance
{
    // Reference identity is private to the manager; callers cannot construct or copy ownership.
    private Model _model;
    private bool _released;

    internal ModelInstance(Model model) => _model = model;

    /// <summary>Borrowed native handle for rendering and animation. Do not unload it or its
    /// resources, or retain it after ReleaseModelInstance or UnloadAll.
    /// Access after release throws ObjectDisposedException.</summary>
    public Model Model
    {
        get
        {
            ObjectDisposedException.ThrowIf(_released, this);
            return _model;
        }
    }

    internal void Invalidate()
    {
        _released = true;
        _model = default;
    }
}
