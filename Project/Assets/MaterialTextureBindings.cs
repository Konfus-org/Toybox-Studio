using System.Collections.Generic;

namespace Toybox.Studio.Project.Assets;

/// <summary>Serialized mirror of the engine's <c>MaterialTextureBindings</c> — a material's texture list. A
/// single-field container struct (the engine flattens it to its <c>values</c> array).</summary>
public sealed class MaterialTextureBindings
{
    public IReadOnlyList<MaterialTextureBinding> Values { get; set; } = [];
}
