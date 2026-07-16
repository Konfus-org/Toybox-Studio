using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.AssetOwners;
using Toybox.Studio.Dialogs;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Composition;
using Toybox.Studio.Viewport;

namespace Toybox.Studio.AssetViewer;

/// <summary>
/// A non-singleton asset editor: shows one asset (a model, a material on a sphere, or a texture on a
/// plane) in its own preview world beside a property inspector that edits it. The editor loads that world
/// (the bundled <c>AssetPreview.world</c>, whose globals hold a key light and the sky) and registers its
/// dependency assets through <see cref="PreviewScene"/>; this view-model then builds the previewed entity
/// in it (by full sync path, via <see cref="PreviewWorld"/>) once the view has started and its world id is
/// known, and separately loads the typed <see cref="Asset"/> mirror it hosts. Deriving from
/// <see cref="AssetOwnerViewModel"/> and calling <see cref="AssetOwnerViewModel.Host"/> gives the
/// dirty-star title, Save/Cancel, and Undo/Redo for free; the <see cref="Inspector"/> grid edits the
/// mirror, whose live <c>asset.set</c> pushes re-render the preview (the entity references the asset by
/// id, so it never needs rebuilding on an edit).
/// </summary>
public sealed partial class AssetViewerViewModel : AssetOwnerViewModel, IDisposable
{
    private readonly AssetViewerLauncher _launcher;
    private readonly Engine _engine;
    private readonly EventDispatcher _events;
    private readonly Popups _popups;
    private readonly Logger _log;
    private readonly ViewModelFactory _viewModels;
    private readonly PreviewScene _scene;
    private readonly AssetPreviewBuilder _builder;

    private AssetEntry? _asset;
    private Asset? _hostedAsset;
    private ViewportStream? _stream;

    // Bumped on every Show so a re-show (reuse mode loads a new asset into this viewer) drops the previous
    // asset's in-flight async build/load rather than corrupting the current preview.
    private int _generation;

    public AssetViewerViewModel(
        AssetViewerLauncher launcher, Engine engine, EventDispatcher events, Logger log, AssetCatalog catalog,
        Popups popups, ViewModelFactory viewModels)
    {
        _launcher = launcher;
        _engine = engine;
        _events = events;
        _log = log;
        _popups = popups;
        _viewModels = viewModels;
        _scene = new PreviewScene(engine);
        _builder = new AssetPreviewBuilder(engine, catalog, log);

        // The kind and two ghost messages are the runtime arguments; the factory injects the viewport's
        // services (EventDispatcher, Logger, the engine it seeds its launch-prompt state from).
        Viewport = viewModels.Create<ViewportViewModel>(
            ViewKind.AssetPreview, "No asset loaded.", "Loading asset…");

        // The asset handles in a material (its base, texture bindings) resolve to a name link that opens
        // the referenced asset back into an Asset Viewer — the picker filters to each property's asset
        // type; a shader parameter's value edits with a kind-typed value editor.
        Inspector = viewModels.Create<PropertyGridViewModel>(new ReflectionPropertyNodeFactory(
            viewModels, new HandleValueEditor(viewModels), new MaterialValueEditor(viewModels)));

        // Register so the launcher can REUSE this viewer for a later open (most-recently-opened wins), and
        // claim the asset a new-window open handed through.
        launcher.RegisterCurrent(Show);
        if (launcher.TakePending() is { } pending)
            Show(pending);
    }

    /// <summary>The surface host the view embeds — the preview view's engine stream + shared GPU texture.</summary>
    public ViewportViewModel Viewport { get; }

    /// <summary>The property inspector over the hosted asset mirror; its edits push live to the engine
    /// (and thereby the preview) and feed the owner's dirty/undo tracking.</summary>
    public PropertyGridViewModel Inspector { get; }

    /// <summary>Whether the hosted asset exposes editable properties. False until an asset with a real
    /// body loads (and stays false for an identity-only asset like a model) — the view then hides the
    /// inspector and the Save/Undo chrome and gives the whole panel to the 3D preview.</summary>
    [ObservableProperty]
    public partial bool IsEditable { get; set; }

    /// <summary>The dock tab title: the hosted asset's name (its dirty star comes from the owner base).</summary>
    protected override string DisplayName => _hostedAsset?.Name is { Length: > 0 } name ? name : _asset?.Name ?? "Asset Viewer";

    /// <summary>The panel supplies its own view (not the owner chrome), so the body is unused; return the
    /// view-model itself so the view can template the inspector + viewport split.</summary>
    public override object? Body => this;

    /// <summary>
    /// Opens an asset: starts its engine preview view (its own world + orbit camera), builds the previewed
    /// entity once the view has started, and loads the typed mirror the inspector edits. Re-entrant so a
    /// reused viewer can switch assets; showing the asset already shown is a no-op.
    /// </summary>
    public void Show(AssetEntry asset)
    {
        if (_asset is { } current && current.Id == asset.Id)
            return;

        var generation = ++_generation;
        _asset = asset;
        OnPropertyChanged(nameof(Title)); // the tab now resolves to the asset's name

        // Release the previously edited mirror (reuse mode switches the asset in place).
        ReleaseHosted();

        // Replacing the stream stops the previous preview view (and releases its world); a fresh view starts
        // here. The preview scene (its dependency assets + the AssetPreview.world) is loaded and released
        // through these hooks, so the bridge owns no editor-specific preview content.
        var stream = new ViewportStream(
            _engine, _events, ViewKind.AssetPreview,
            provisionWorld: _scene.LoadAsync, releaseWorld: _scene.ReleaseAsync);
        _stream = stream;
        stream.ViewStarted += () => OnViewStarted(stream, asset, generation);
        Viewport.Prepare(stream);

        // Show the loading ghost over the (still empty, or reused) preview until the new view's first frame
        // lands — the isolated world start + entity build + model stream can take a beat, and a blank panel
        // in the meantime reads as broken. The viewport clears this itself once a frame arrives.
        Viewport.IsPreparing = true;

        HostAssetAsync(asset, generation).FireAndForget();
    }

    public void Dispose()
    {
        _launcher.UnregisterCurrent(Show);
        ReleaseHosted();
        Viewport.Dispose(); // stops the engine view
    }

    // Loads the typed mirror for the row and, once it has hydrated, hosts it (dirty/save/undo) and shows
    // it in the inspector. Loading off the UI thread but resuming on it (the grid + owner state are
    // UI-bound); a newer Show supersedes and unbinds the stale mirror.
    private async Task HostAssetAsync(AssetEntry entry, int generation)
    {
        if (AssetLoader.Load(entry) is not { } asset)
        {
            ReportError($"Can't edit '{entry.Name}': no editable asset type is registered for '{entry.Type}'.");
            return;
        }

        var loaded = await asset.Loaded.ContinueOnSameContext();
        if (generation != _generation)
        {
            asset.Unbind();
            return;
        }
        if (!loaded)
        {
            ReportError($"Couldn't load '{entry.Name}' for editing: {loaded.Error}");
            asset.Unbind();
            return;
        }

        _hostedAsset = asset;
        Host(asset);            // dirty-star title, Save/Cancel, Undo/Redo
        Inspector.Show(asset);  // editable inspector; edits push live and re-render the preview

        // An identity-only asset (a model) has no editable rows — show it as a pure preview, no inspector
        // or edit chrome.
        IsEditable = Inspector.HasNodes;
        OnPropertyChanged(nameof(Title));
    }

    // Detaches and unbinds the currently hosted mirror (owner base + inspector + sync hub).
    private void ReleaseHosted()
    {
        Host(null);
        Inspector.Show(null);
        IsEditable = false; // preview-only until the next asset loads and reports its rows
        _hostedAsset?.Unbind();
        _hostedAsset = null;
    }

    // Ends the loading-ghost ("preparing") state, but only for the generation that set it — a stale build
    // completing or failing must not clear a ghost a newer Show has just turned back on. Marshalled to the
    // UI thread since it drives a bound property and callers run off the engine's RPC lane.
    private void StopPreparing(int generation) =>
        Dispatch.To(DispatchContext.UI, () =>
        {
            if (generation == _generation)
                Viewport.IsPreparing = false;
        });

    // Surfaces a failure to the user (a popup) as well as the log — nothing in the viewer should break
    // silently. Marshalled to the UI thread since the popup is modal and some callers run off the RPC lane.
    private void ReportError(string message)
    {
        _log.Error($"Asset viewer: {message}");
        Dispatch.To(DispatchContext.UI, () => _popups.ErrorAsync("Asset Viewer", message).FireAndForget());
    }

    // The view has started (its world id is known): build the previewed entity, unless a newer Show has
    // superseded this one. Fires off the engine's RPC thread, so the async build runs there — its only
    // shared reads are guarded by the generation.
    private void OnViewStarted(ViewportStream stream, AssetEntry asset, int generation)
    {
        if (generation != _generation || stream.WorldAssetId == 0)
            return;

        BuildPreviewAsync(stream, asset, generation).FireAndForget();
    }

    private async Task BuildPreviewAsync(ViewportStream stream, AssetEntry asset, int generation)
    {
        // The editor already loaded the bundled AssetPreview.world (its globals hold the key light and the
        // sky) via the stream's provision hook, so the preview already sits against the authored skybox —
        // the shared builder only adds the previewed asset's own entity.
        var world = new PreviewWorld(_engine, stream.WorldAssetId);
        var built = await _builder
            .BuildAsync(world, asset, () => generation == _generation)
            .ContinueOnAnyContext();
        if (!built)
        {
            ReportError($"Couldn't build the preview for '{asset.Name}': {built.Error}");
            StopPreparing(generation); // no frame will land, so drop the loading ghost ourselves
            return;
        }
        if (generation != _generation)
            return;

        // The engine streams the model in asynchronously, so the first frame can land before the renderable
        // has bounds; re-frame a few times over the next second so the orbit camera settles once it loads.
        for (var attempt = 0; attempt < 4 && generation == _generation; attempt++)
        {
            await stream.FrameAsync().ContinueOnAnyContext();
            await Task.Delay(250).ContinueOnAnyContext();
        }

        // The first frame normally clears the loading ghost on its own; this is a backstop for the case
        // where a surface never flips HasFrames, so our build finishing still drops the spinner.
        StopPreparing(generation);
    }
}
