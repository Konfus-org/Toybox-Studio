namespace Toybox.Studio.Assets;

/// <summary>
/// A model asset (<c>.fbx</c>/<c>.obj</c>/<c>.gltf</c>/<c>.glb</c>), mirrored from the engine's
/// <c>Model</c>. Identity-only: the geometry, part hierarchy, and material slots are populated by the
/// engine's model loader from the source file (a version-only asset — none of it serializes), so there
/// is nothing beyond the <see cref="Asset"/> identity to mirror.
/// </summary>
public sealed class Model : Asset
{
    public Model(ulong id = 0) : base(id)
    {
    }
}
