using Toybox.Studio.EngineApi.Types.Assets;

namespace Toybox.Studio.AssetViewer;

/// <summary>
/// Classifies an asset by its extension token (<see cref="AssetEntry.Type"/>) for File ▸ Open ▸ Asset
/// routing, resolving the token to its asset class through <see cref="AssetKinds"/> — whose extension
/// sets are declared on the asset classes themselves (<see cref="AssetExtensionsAttribute"/> /
/// <see cref="CreatableAttribute"/>), not hardcoded here.
/// </summary>
public static class AssetClassifier
{
    public static bool IsModel(string type) => AssetKinds.Is<Model>(type);

    public static bool IsTexture(string type) => AssetKinds.Is<Texture>(type);

    public static bool IsMaterial(string type) =>
        AssetKinds.Is<Material>(type) || AssetKinds.Is<MaterialInstance>(type);

    /// <summary>Whether the asset is a world — opened into the World Viewer (the active editing world),
    /// not the Asset Viewer. World assets live in the Ecs assembly, so they aren't in the extension
    /// registry <see cref="AssetKinds"/> scans; the token is matched directly.</summary>
    public static bool IsWorld(AssetEntry asset) =>
        string.Equals(asset.Type, "world", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the Asset Viewer can render this asset (model, texture, or material).</summary>
    public static bool IsPreviewable(AssetEntry asset) =>
        IsModel(asset.Type) || IsTexture(asset.Type) || IsMaterial(asset.Type);
}
