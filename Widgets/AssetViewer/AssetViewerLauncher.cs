using Toybox.Studio.Services.Project;
using Toybox.Studio.Shell.Workspace;

namespace Toybox.Studio.Widgets.AssetViewer;

/// <summary>
/// Bridges "open this asset in a viewer" to a freshly-spawned <see cref="AssetViewerView"/> dockable.
/// The window manager builds a non-singleton view-model from DI with no arguments, so the target asset
/// is stashed here and claimed by the spawning view-model's constructor (the open is synchronous).
/// Mirrors <see cref="Toybox.Studio.Services.Scripting.ScriptEditorLauncher"/> as a small seam so the
/// shell doesn't reach into the workspace and the viewer view-model directly.
/// </summary>
public sealed class AssetViewerLauncher
{
    private const string ViewerDockableId = "AssetViewer";

    private readonly WorkspaceViewModel _workspace;
    private Asset? _pending;

    public AssetViewerLauncher(WorkspaceViewModel workspace) => _workspace = workspace;

    /// <summary>Opens a new Asset Viewer panel previewing <paramref name="asset"/>.</summary>
    public void Open(Asset asset)
    {
        // OpenDockable spawns the view-model synchronously, which claims this pending asset via
        // TakePending; clear any unclaimed remainder so a later layout-restore spawn can't pick it up.
        _pending = asset;
        _workspace.OpenDockable(ViewerDockableId);
        _pending = null;
    }

    /// <summary>Claimed by a spawning <see cref="AssetViewerViewModel"/> to learn which asset to load
    /// (null when the panel is rematerialized by a layout restore rather than an explicit open).</summary>
    public Asset? TakePending()
    {
        var asset = _pending;
        _pending = null;
        return asset;
    }
}
