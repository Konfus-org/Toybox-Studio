using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;

namespace Toybox.Studio.Ecs.Components;

/// <summary>
/// One level-of-detail entry: the model used within a distance band. The serialized mirror of the engine's
/// <c>Lod</c> struct — a model handle plus the max camera distance the band covers. A plain value type (no
/// <see cref="EngineSyncAttribute"/>): it folds into its owning <see cref="Lods"/> component's bare JSON, and the
/// grid renders it from these properties + attributes.
/// </summary>
public sealed class Lod
{
    [AssetExtensions("fbx", "obj", "gltf", "glb")]
    public AssetHandle Handle { get; set; } = AssetHandle.None;

    public float MaxDistance { get; set; }
}
