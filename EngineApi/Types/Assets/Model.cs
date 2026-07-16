using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// A model asset (<c>.fbx</c>/<c>.obj</c>/<c>.gltf</c>/<c>.glb</c>), mirrored from the engine's
/// <c>Model</c>. Its geometry and part hierarchy stay engine-side (runtime-only), but its material
/// <see cref="Slots"/> mirror in through the standard asset describe — the engine's model reader
/// populates them from the source file and the describe body carries them, so the editor reads a model's
/// material list off this mirror like any other synced value (no bespoke query).
/// </summary>
[AssetExtensions("fbx", "obj", "gltf", "glb", "dae", "mesh")]
public sealed partial class Model : Asset
{
    public Model(ulong id = 0) : base(id)
    {
        Mode = MeshMode.Static;
        Meshes = [];
        Parts = [];
    }

    /// <summary>Whether the geometry is shared/immutable or runtime-mutable (the engine's
    /// <c>Model::mode</c>). Engine → studio only.</summary>
    [EngineSync(Mode = SyncMode.OneWayFromEngine)]
    public partial MeshMode Mode { get; private set; }

    /// <summary>The model's meshes — vertex/index buffers + bounds (the engine's <c>Model::meshes</c>).
    /// Engine → studio only: the model reader builds them from the source file.</summary>
    [EngineSync(Mode = SyncMode.OneWayFromEngine, Converter = typeof(MeshConverter))]
    public partial IReadOnlyList<Mesh> Meshes { get; private set; }

    /// <summary>The model's part hierarchy — each a mesh under a transform + material slot (the engine's
    /// <c>Model::parts</c>). Engine → studio only.</summary>
    [EngineSync(Mode = SyncMode.OneWayFromEngine, Converter = typeof(ModelPartConverter))]
    public partial IReadOnlyList<ModelPart> Parts { get; private set; }

    /// <summary>The model's per-slot material identity handles (the engine's <c>Model::slots</c>), in
    /// slot-index order. Engine → studio only: the model reader derives them from the source file's
    /// materials, so the editor never pushes them back.</summary>
    [EngineSync(Mode = SyncMode.OneWayFromEngine, Converter = typeof(HandleListConverter))]
    public partial IReadOnlyList<Handle> Slots { get; private set; }
}
