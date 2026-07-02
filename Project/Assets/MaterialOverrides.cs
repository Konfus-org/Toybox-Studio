using System.Collections.Generic;

namespace Toybox.Studio.Project.Assets;

/// <summary>
/// Serialized mirror of the engine's <c>MaterialOverrides</c> — the per-instance texture and parameter overrides a
/// <see cref="MaterialInstance"/> layers onto its base material. A plain value type.
/// </summary>
public sealed class MaterialOverrides
{
    public IReadOnlyList<MaterialTextureBinding> Textures { get; set; } = [];

    public IReadOnlyList<MaterialParameter> Parameters { get; set; } = [];
}
