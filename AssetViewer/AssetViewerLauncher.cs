using Toybox.Studio.Docking;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.Settings;

namespace Toybox.Studio.AssetViewer;

/// <summary>
/// The reuse-or-new seam for the Asset Viewer. The panel is a non-singleton (each preview owns its own
/// world/stream), so <see cref="WorkspaceViewModel.Open{T}"/> always spawns a fresh one; this decides —
/// per the <see cref="OpenAssetsMode"/> setting — whether to instead load the asset into the
/// most-recently-opened viewer (<see cref="OpenAssetsMode.ReuseViewport"/>) and bring it forward. A
/// spawning view-model registers its <c>Show</c> here and claims the pending asset.
/// </summary>
public sealed class AssetViewerLauncher(WorkspaceViewModel workspace, SettingsManager settings)
{
    private AssetEntry? _pending;
    private Action<AssetEntry>? _showCurrent;

    /// <summary>Opens the asset: into the existing viewer when reuse is on and one is open, else a new one.</summary>
    public void Open(AssetEntry asset)
    {
        var reuse = settings.Editor.EditorAssetSettings.OpenAssetsMode == OpenAssetsMode.ReuseViewport;

        // Reuse only when an Asset Viewer is genuinely still open: Focus returns false once the last one
        // has closed, so a stale reuse delegate (a viewer that was closed without clearing itself) can't
        // swallow the open into an invisible, disposed viewer — the cause of the "sometimes it opens,
        // sometimes it doesn't" flakiness. When it returns false we fall through and spawn a fresh one.
        if (reuse && _showCurrent is { } show && workspace.Focus<AssetViewerViewModel>())
        {
            show(asset);
            return;
        }

        OpenInNewWindow(asset);
    }

    /// <summary>Spawns a fresh Asset Viewer showing the asset (the new view-model claims it via
    /// <see cref="TakePending"/>).</summary>
    public void OpenInNewWindow(AssetEntry asset)
    {
        _pending = asset;
        workspace.Open<AssetViewerViewModel>();
        _pending = null;
    }

    /// <summary>The asset a just-spawned viewer should show (claimed once), or null.</summary>
    public AssetEntry? TakePending()
    {
        var pending = _pending;
        _pending = null;
        return pending;
    }

    /// <summary>A viewer registers its <c>Show</c> as the current reuse target (most recent wins).</summary>
    public void RegisterCurrent(Action<AssetEntry> show) => _showCurrent = show;

    /// <summary>A viewer drops itself as the reuse target on close (only if it is still the current one).</summary>
    public void UnregisterCurrent(Action<AssetEntry> show)
    {
        if (_showCurrent == show)
            _showCurrent = null;
    }
}
