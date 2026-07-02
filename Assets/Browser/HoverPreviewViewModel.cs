using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Logging;
using Toybox.Studio.Project;
using Toybox.Studio.Worlds;
using Toybox.Studio.Utils;
using Toybox.Studio.AssetViewer;
using Toybox.Studio.Viewport;

namespace Toybox.Studio.AssetBrowser;

/// <summary>
/// The browser's hover card. It is a thin adapter over an <em>embedded</em> <see cref="AssetViewerViewModel"/>
/// — the very same view-model the dockable Asset Viewer uses — so the hover preview resolves and renders every
/// asset kind (model on a checkerboard, material on a sphere, texture on a plane, sky as the environment)
/// exactly the way the full viewer does. The embedded viewer runs as a low-resolution auto-orbiting turntable
/// and stays out of the dock/launcher machinery. Showing a tile retargets the single underlying preview view;
/// the card also surfaces the hovered tile for its text (name/type/scale/path).
/// </summary>
public sealed partial class HoverPreviewViewModel : ObservableObject, IDisposable
{
    private readonly AssetViewerViewModel _viewer;

    public HoverPreviewViewModel(
        AssetViewerLauncher launcher, Session session, Engine engine,
        IEngineSyncScheduler scheduler, AssetCatalog catalog, WorldSelection selection, EngineWatcher watcher,
        Logger logger)
    {
        _viewer = new AssetViewerViewModel(
            launcher, session, engine, scheduler, catalog, selection, watcher, logger, embedded: true);
        _viewer.Surface.PropertyChanged += OnSurfacePropertyChanged;
    }

    /// <summary>The tile being previewed (drives the card's name/type/scale/path); null hides the card.</summary>
    [ObservableProperty]
    public partial AssetTileViewModel? Current { get; set; }

    /// <summary>The streamed engine-view surface the card's interop control binds to.</summary>
    public ViewportSurfaceViewModel Surface => _viewer.Surface;

    /// <summary>Whether a real frame is on screen yet (else the card shows a loading ghost).</summary>
    public bool HasFrames => _viewer.Surface.HasFrames;

    /// <summary>Previews <paramref name="tile"/>'s asset, retargeting the single embedded preview view. A
    /// re-hover of the same asset is a no-op (the viewer de-dupes).</summary>
    public void Show(AssetTileViewModel tile)
    {
        Current = tile;
        _viewer.Show(tile.Asset);
    }

    /// <summary>Hides the card (the pointer left the grid, or hovered a non-previewable tile). The embedded
    /// view stays alive, ready to be retargeted on the next hover.</summary>
    public void ClearCurrent() => Current = null;

    public void Dispose()
    {
        _viewer.Surface.PropertyChanged -= OnSurfacePropertyChanged;
        _viewer.Dispose();
    }

    private void OnSurfacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewportSurfaceViewModel.HasFrames))
            Dispatch.To(DispatchContext.UI, () => OnPropertyChanged(nameof(HasFrames)));
    }
}
