using System.Collections.Generic;
using System.Linq;
using Toybox.Studio.Project.Assets;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Decides which of a <see cref="Material"/> asset's top-level fields are relevant to its render category, so
/// the inspector can hide the ones that don't apply to the chosen <see cref="MaterialType"/>. Most fields
/// (the type selector, shader, parameter and texture bindings) apply to every material; only fields listed in
/// <see cref="Restricted"/> are category-specific. Anything not listed shows for all types, so a field the
/// engine adds later is visible by default rather than silently hidden.
/// </summary>
public static class MaterialFields
{
    private static readonly IReadOnlyDictionary<string, MaterialType[]> Restricted =
        new Dictionary<string, MaterialType[]>(System.StringComparer.OrdinalIgnoreCase)
        {
            // The render config is raster surface state (depth test/write, blend, two-sided, cull, shadow). It
            // only means something for a material that rasterizes a surface — a raster material or a geometry/
            // depth pass — so it's hidden for sky (drawn as the background), full-screen post, and compute.
            ["config"] = [MaterialType.Raster, MaterialType.Geo],
        };

    /// <summary>Whether the field with the given wire name (e.g. "config") should show for the given type.</summary>
    public static bool IsVisible(MaterialType type, string field) =>
        !Restricted.TryGetValue(field, out var types) || types.Contains(type);
}
