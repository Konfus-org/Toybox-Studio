using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.Project;

/// <summary>
/// Typed handle for an engine <c>Material</c> asset body. A material's editable surface is its shader
/// parameter / texture bindings (shader-specific), so it round-trips whole through <see cref="Raw"/> and is
/// edited via that body; the typed handle lets code address it string-free (<c>asset.Get&lt;Material&gt;()</c>).
/// </summary>
public sealed record Material : IAssetType<Material>
{
    /// <summary>The asset body this was read from, round-tripped untouched on save.</summary>
    public JObject? Raw { get; init; }

    public JObject ToAssetJson() => Raw is null ? new JObject() : (JObject)Raw.DeepClone();

    public static Material FromAssetJson(JObject body) => new() { Raw = body };
}
