using Toybox.Studio.AssetViewer;

namespace Toybox.Studio.Shell.Workspace;

/// <summary>
/// A <see cref="WorkspaceTool"/> that carries a per-instance <see cref="AssetViewerState"/>. Because the layout
/// serializer round-trips the whole tool tree (type-tagged, stripping only live content), this state rides
/// along with the saved layout — so each asset viewer remembers which asset it was showing across restarts.
/// The window manager creates one of these for any dockable whose view-model is an <see cref="IAssetViewerHost"/>.
/// Mirrors <see cref="ToolbarTool"/>.
/// </summary>
public sealed class AssetViewerTool : WorkspaceTool
{
    public AssetViewerState Asset { get; set; } = new();
}
