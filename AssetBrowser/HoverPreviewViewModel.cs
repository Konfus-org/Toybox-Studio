using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.AssetViewer;
using Toybox.Studio.EngineApi;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Composition;
using Toybox.Studio.Viewport;

namespace Toybox.Studio.AssetBrowser;

/// <summary>
/// The Asset Browser's live hover preview: a small, auto-orbiting 3D turntable of whatever previewable asset
/// (model, material, texture) the pointer is over, shown in the floating <c>CursorTooltip</c> card. It reuses
/// the full preview stack — its own <see cref="ViewportViewModel"/> streaming a <see cref="ViewKind.AssetPreview"/>
/// view, a <see cref="PreviewScene"/> that provisions the bundled preview world, and the shared
/// <see cref="AssetPreviewBuilder"/> that populates the previewed entity — but with no inspector or edit
/// chrome: it is a read-only glance, torn down the moment the pointer leaves (<see cref="Clear"/>), so no
/// engine preview world lingers while the browser sits idle. The turntable spins engine-side (the view is
/// started with <c>turntable: true</c>) at a cheap quarter render scale.
/// </summary>
public sealed partial class HoverPreviewViewModel : ObservableObject, IDisposable
{
    private const double RenderScale = 0.25;

    private readonly Engine _engine;
    private readonly EventDispatcher _events;
    private readonly PreviewScene _scene;
    private readonly AssetPreviewBuilder _builder;

    private ViewportStream? _stream;

    // Bumped on every Show/Clear so a superseded hover drops the previous asset's in-flight build rather than
    // writing it into a released (or reused) preview world.
    private int _generation;

    public HoverPreviewViewModel(
        Engine engine, EventDispatcher events, AssetCatalog catalog, Logger log, ViewModelFactory viewModels)
    {
        _engine = engine;
        _events = events;
        _scene = new PreviewScene(engine);
        _builder = new AssetPreviewBuilder(engine, catalog, log);

        // A plain asset-preview viewport (no toolbars, no pick handler); the card embeds its ViewportView.
        Viewport = viewModels.Create<ViewportViewModel>(ViewKind.AssetPreview, string.Empty, "Loading…");
    }

    /// <summary>The turntable surface the card embeds (a <c>ViewportView</c> binds to this).</summary>
    public ViewportViewModel Viewport { get; }

    /// <summary>The asset currently previewed, or null when nothing is hovered — the card's open state binds
    /// to this being non-null, and its name/type/path text to this row.</summary>
    [ObservableProperty]
    public partial AssetEntry? Current { get; private set; }

    /// <summary>
    /// Shows the turntable for the hovered asset: starts a fresh throwaway preview view (its own provisioned
    /// world + auto-orbit camera) and builds the previewed entity once it starts. Re-entrant — moving to a new
    /// tile switches the asset; re-hovering the same one is a no-op.
    /// </summary>
    public void Show(AssetEntry asset)
    {
        if (Current is { } current && current.Id == asset.Id)
            return;

        var generation = ++_generation;
        Current = asset;

        // Replacing the stream stops the previous view (and releases its world). The preview scene loads the
        // bundled AssetPreview.world + its dependency assets through these hooks; the engine auto-orbits.
        var stream = new ViewportStream(
            _engine, _events, ViewKind.AssetPreview,
            provisionWorld: _scene.LoadAsync, releaseWorld: _scene.ReleaseAsync,
            turntable: true, renderScale: RenderScale);
        _stream = stream;
        stream.ViewStarted += () => OnViewStarted(stream, asset, generation);
        Viewport.Prepare(stream);
        Viewport.IsPreparing = true;
    }

    /// <summary>Hides the card and stops the preview view — the pointer left the grid (or a non-previewable
    /// tile). Abandons any in-flight build so it can't write into the world being released.</summary>
    public void Clear()
    {
        _generation++;
        Current = null;
        _stream = null;
        Viewport.Detach();       // stops the engine view + releases the preview world
        Viewport.IsPreparing = false;
    }

    public void Dispose() => Viewport.Dispose();

    // The view has started (its provisioned world id is known): build the previewed entity, unless a newer
    // hover has superseded this one. Fires off the engine's RPC thread.
    private void OnViewStarted(ViewportStream stream, AssetEntry asset, int generation)
    {
        if (generation != _generation || stream.WorldAssetId == 0)
            return;

        BuildAsync(stream, asset, generation).FireAndForget();
    }

    private async Task BuildAsync(ViewportStream stream, AssetEntry asset, int generation)
    {
        var world = new PreviewWorld(_engine, stream.WorldAssetId);
        var built = await _builder
            .BuildAsync(world, asset, () => generation == _generation)
            .ContinueOnAnyContext();
        if (!built)
        {
            StopPreparing(generation); // no frame will land, so drop the loading ghost ourselves
            return;
        }

        // The model streams in asynchronously; re-frame a few times so the orbit camera settles once it lands.
        for (var attempt = 0; attempt < 4 && generation == _generation; attempt++)
        {
            await stream.FrameAsync().ContinueOnAnyContext();
            await Task.Delay(250).ContinueOnAnyContext();
        }

        StopPreparing(generation);
    }

    // Ends the loading-ghost state, but only for the generation that set it (a stale build finishing must not
    // clear a ghost a newer hover just turned back on). Marshalled to the UI thread — it drives a bound
    // property and callers run off the engine's RPC lane.
    private void StopPreparing(int generation) =>
        Dispatch.To(DispatchContext.UI, () =>
        {
            if (generation == _generation)
                Viewport.IsPreparing = false;
        });
}
