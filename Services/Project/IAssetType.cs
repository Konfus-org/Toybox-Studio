using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.Project;

/// <summary>
/// A typed, string-free view of a loaded engine asset's editable body — the asset counterpart of
/// <c>IComponentType</c>. Implementations are small hand-written records that (de)serialize the asset's
/// fields to/from the engine's self-describing JSON, so code can do <c>asset.Get&lt;Material&gt;()</c>.
/// </summary>
/// <typeparam name="TSelf">The implementing record itself (CRTP), so the static factory can return it.</typeparam>
public interface IAssetType<out TSelf>
    where TSelf : IAssetType<TSelf>
{
    /// <summary>Reconstructs the typed asset from its describe body.</summary>
    static abstract TSelf FromAssetJson(JObject body);

    /// <summary>Serializes the typed asset back to the engine body for <c>asset.save</c>.</summary>
    JObject ToAssetJson();
}
