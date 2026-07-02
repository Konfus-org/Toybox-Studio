using Newtonsoft.Json;

namespace Toybox.Studio.Settings;

/// <summary>
/// Asset Browser preferences. Currently the user-defined category list that drives the browser's collection
/// rail and how each asset is grouped and iconed. Edited in Settings ▸ Editor ▸ Asset Browser.
/// </summary>
public sealed class AssetBrowserEditorSettings
{
    /// <summary>
    /// The categories shown as filters in the Asset Browser, in order. The first category whose
    /// <see cref="AssetCategory.Extensions"/> matches an asset claims it — so list more specific categories
    /// first (e.g. "Skies" before "Materials"); anything unmatched falls into the built-in "Other" bucket.
    /// Replaced wholesale on load/save (not merged into the defaults) so edits and removals stick.
    /// </summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<AssetCategory> Categories { get; set; } = AssetCategory.Defaults();
}
