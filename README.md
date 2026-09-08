# RaylibGameFramework.Assets

AssetManager owns Texture, Font, Model and ModelAnimations resources. Load and
unload on the graphics thread while the Raylib window is alive.

## Model animations

Register the model and its animations under separate logical keys (paths are
resolved relative to AppContext.BaseDirectory):

```json
{
  "Assets": [
    { "Key": "pedestrian", "Type": "Model", "Path": "Assets/Models/pedestrian.glb" },
    { "Key": "pedestrian.animations", "Type": "ModelAnimations", "Path": "Assets/Models/pedestrian.glb" }
  ]
}
```

```csharp
assets.RequireAsset("pedestrian.animations");
while (!assets.ProcessNext()) { }

var instance = assets.CreateModelInstance("pedestrian");
ReadOnlySpan<ModelAnimation> animations = assets.GetModelAnimations("pedestrian.animations");
// For the pedestrian GLB with its single Run clip:
Raylib.UpdateModelAnimation(instance.Model, animations[0], frame);
Raylib.DrawModel(instance.Model, position, 1f, Color.White);

// When this entity no longer needs its model:
assets.ReleaseModelInstance(instance);
```

CreateModelInstance loads a fresh validated model from the Model catalogue path,
even if the shared model is not required or loaded. Each sealed ModelInstance has
its own native allocation and private reference identity. Its read-only Model
property lends the native handle; there is no public constructor or unload method.
Only the creating manager may release it. Double or foreign release throws
InvalidOperationException; accessing Model after release throws ObjectDisposedException.
Previously borrowed handles must also no longer be used or unloaded.

Instances do not affect LoadedCount, requirements, transition progress or
ProcessNext. UnloadAll releases instances before shared catalogue assets.
GetModel remains the unchanged shared API for static/shared use. Independently
animated entities should each use CreateModelInstance. Animation clips stay shared;
keep the animation key resident for as long as it is used.

Run native integration tests with a graphics context available:
`dotnet test tests/ModelInstanceTests.csproj -c Release`.

The caller selects a clip and supplies a frame within that clip's FrameCount.
No animation controller or playback timing is provided.

GetModelAnimations throws KeyNotFoundException if the animation key is not loaded.
The span and animation handles are borrowed and remain valid only until the
manager unloads that key. Do not unload them or retain them after unloading.
AssetManager keeps the native allocation and releases it with
Raylib.UnloadModelAnimations.

Each animation collection counts as one asset in LoadedCount and transition
progress. Files with no animations fail loading and remain pending. Require,
release, SetRequiredAssets and ClearRequiredAssets follow the same rules as
other asset types: ProcessNext unloads released resources before loading required
ones. UnloadAll immediately releases all resources and clears requirements;
call it before closing the window.
