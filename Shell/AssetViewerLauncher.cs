using System;
using Toybox.Studio.Shell.Workspace;
using Toybox.Studio.AssetViewer;

namespace Toybox.Studio.Project;

/// <summary>
/// Bridges "open this asset in a viewer" to the dockable asset viewer. By default it REUSES the asset viewer
/// that's already open (updating its previewed asset and bringing it to the front) so repeated opens don't
/// pile up tabs; <see cref="OpenInNewWindow"/> always spawns a fresh one. A spawned viewer registers its
/// <see cref="RegisterCurrent"/> show-callback so the reuse path drives it through that seam rather than the
/// view-model's API; the <see cref="AssetViewerViewModel"/> type is named only to identify the dockable.
/// Mirrors <see cref="Scripting.ScriptEditorLauncher"/>.
/// </summary>
public sealed class AssetViewerLauncher
{
    private readonly WorkspaceViewModel _workspace;
    private AssetMeta? _pending;
    private Action<AssetMeta>? _showCurrent;

    public AssetViewerLauncher(WorkspaceViewModel workspace) => _workspace = workspace;

    /// <summary>
    /// Opens <paramref name="asset"/> in the existing asset viewer if one is open — reusing it and bringing it
    /// to the front — otherwise opens it in a new one.
    /// </summary>
    public void Open(AssetMeta asset)
    {
        if (_showCurrent is { } show)
        {
            show(asset);
            _workspace.FocusExisting<AssetViewerViewModel>();
        }
        else
        {
            OpenInNewWindow(asset);
        }
    }

    /// <summary>Always opens <paramref name="asset"/> in a new asset-viewer panel.</summary>
    public void OpenInNewWindow(AssetMeta asset)
    {
        // OpenDockable spawns the view-model synchronously, which claims this pending asset via TakePending;
        // clear any unclaimed remainder so a later layout-restore spawn can't pick it up.
        _pending = asset;
        _workspace.OpenDockable<AssetViewerViewModel>();
        _pending = null;
    }

    /// <summary>Claimed by a spawning asset-viewer view-model to learn which asset to load (null when the panel
    /// is rematerialized by a layout restore rather than an explicit open).</summary>
    public AssetMeta? TakePending()
    {
        var asset = _pending;
        _pending = null;
        return asset;
    }

    /// <summary>An asset viewer registers its <c>Show</c> here on construction so a default <see cref="Open"/>
    /// reuses it; the most recently opened viewer wins.</summary>
    public void RegisterCurrent(Action<AssetMeta> show) => _showCurrent = show;

    /// <summary>An asset viewer drops its registration on close so a later open spawns a fresh one.</summary>
    public void UnregisterCurrent(Action<AssetMeta> show)
    {
        if (_showCurrent == show)
            _showCurrent = null;
    }
}
