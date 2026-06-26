using Toybox.Studio.Widgets.AssetViewer;

namespace Toybox.Studio.Shell.Workspace;

/// <summary>
/// A dockable view-model that previews a single asset and remembers which one across restarts. The window
/// manager creates an <see cref="AssetViewerTool"/> for such dockables and binds the tool's persisted
/// <see cref="AssetViewerState"/> into the view-model when the tool materializes. Mirrors
/// <see cref="IToolbarHost"/>.
/// </summary>
public interface IAssetViewerHost
{
    /// <summary>
    /// Binds the persisted asset state the hosting dock tool owns. Called once per materialized tool;
    /// idempotent across re-templating passes. On an explicit open the view-model already has its asset and
    /// records it into the state (so a layout save persists it); on a layout restore the view-model reads
    /// the remembered asset back and reloads it once the project has fully loaded.
    /// </summary>
    void BindAsset(AssetViewerState state);
}
