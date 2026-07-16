using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.Settings;

namespace Toybox.Studio.AssetBrowser;

/// <summary>
/// Decides which category a catalog row belongs to. A category claims an asset when its
/// <see cref="AssetBrowserCategory.FileNamePattern"/> matches the asset's file name or path — the usual
/// extension filter. A meta-keyed category (one with only a <see cref="AssetBrowserCategory.MetaPattern"/>,
/// like the default "Scripts") keys off the single meta signal the row carries — <see cref="AssetEntry.IsScript"/>
/// — because the row doesn't carry the <c>.meta</c> sidecar text the pattern would test; full meta-text
/// matching is a later addition (it needs a sidecar read).
/// </summary>
internal static class AssetCategoryMatcher
{
    public static bool Matches(AssetBrowserCategory category, AssetEntry asset)
    {
        // A filename match claims it (an empty pattern never matches, so this is safe to test first) — this
        // lets a category carry both a filename pattern and a meta pattern, e.g. Scripts folding in clang
        // config files by name as well as script sources by meta.
        if (category.FileNamePattern.IsMatch(asset.Name) || category.FileNamePattern.IsMatch(asset.Path))
            return true;

        // A meta-keyed pattern tests the .meta sidecar text, which the row doesn't carry — approximate it
        // with the one meta signal it does: whether the engine classified it as a script.
        return !category.MetaPattern.IsEmpty && asset.IsScript;
    }

    /// <summary>
    /// Whether an asset is part of the open project's own content tree — what the browser should show. The
    /// engine reports every registered asset across all roots (the project, the engine's shared resources,
    /// the editor's asset-viewer built-ins) as absolute, forward-slashed paths. Project content is an asset
    /// that lives in a <em>subfolder</em> under the project root: this drops the project's own top-level
    /// files (AppSettings.json, CMakePresets.json, …) and everything outside the project (engine resources,
    /// the asset-viewer set). The dropped rows stay in the catalog so pickers/handles still resolve them by
    /// id. Comparison normalises slashes and is case-insensitive (Windows paths); an unknown project root
    /// keeps the asset (the catalog is empty without a connected project anyway).
    /// </summary>
    public static bool IsProjectContent(AssetEntry asset, string projectRoot)
    {
        var root = projectRoot.Replace('\\', '/').TrimEnd('/');
        if (root.Length == 0)
            return true;

        var path = asset.Path.Replace('\\', '/');
        if (!path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            return false;

        // A top-level project file has no further separator after the root; real content sits in a subfolder.
        return path.IndexOf('/', root.Length + 1) >= 0;
    }
}
