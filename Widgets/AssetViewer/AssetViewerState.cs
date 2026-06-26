namespace Toybox.Studio.Widgets.AssetViewer;

/// <summary>
/// The per-instance asset-viewer state that persists in the saved dock layout (carried by the
/// <c>AssetViewerTool</c> the window manager creates for an <c>IAssetViewerHost</c>). It remembers which
/// asset the viewer was showing so the panel can reload it when the project is reopened.
///
/// The asset is identified by its project-relative <see cref="AssetPath"/>, not its id: ids are assigned
/// per engine session and are not stable across restarts, whereas the path is. <see cref="AssetName"/> is
/// kept only so the dock tab can show the remembered name before the (async) reload resolves.
/// </summary>
public sealed class AssetViewerState
{
    /// <summary>Project-relative path of the asset last shown, or null when the viewer held nothing.</summary>
    public string? AssetPath { get; set; }

    /// <summary>The asset's display name when last shown, used only for the tab title during a restore.</summary>
    public string? AssetName { get; set; }
}
