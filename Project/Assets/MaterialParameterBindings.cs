using System.Collections.Generic;

namespace Toybox.Studio.Project.Assets;

/// <summary>Serialized mirror of the engine's <c>MaterialParameterBindings</c> — a material's parameter list. A
/// single-field container struct (the engine flattens it to its <c>values</c> array).</summary>
public sealed class MaterialParameterBindings
{
    public IReadOnlyList<MaterialParameter> Values { get; set; } = [];
}
