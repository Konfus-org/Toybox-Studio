using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Services.World;
using Toybox.Studio.Services.World.Components;
using Toybox.Studio.Shell.Panels;
using Toybox.Studio.Shell.Workspace;
using Toybox.Studio.Utils;
using Toybox.Studio.Widgets.Viewport;

namespace Toybox.Studio.Widgets.AssetViewer;

/// <summary>
/// A non-singleton preview panel: shows one asset (a texture on a plane, a material on a sphere, a model,
/// or a sky material as the environment) in its own isolated engine preview world. The bridge seeds that
/// world with a light + the sky assets; THIS view-model builds and configures the previewed entity (and the
/// background sky) through the editor's world/entity API (<see cref="World"/>/<see cref="Entity"/> + the
/// typed components), targeting the preview world by its id. It reuses the viewport's GPU-texture surface
/// (<see cref="ViewportStream"/>) and orbit input; clicking an entity updates the shared
/// <see cref="WorldSelection"/> so the Inspector shows it.
/// </summary>
public sealed partial class AssetViewerViewModel : DataPanel, IDisposable, IViewportInputSink, IAssetViewerHost
{
    private readonly Session _session;
    private readonly EngineRpc _engine;
    private readonly WorldManager _worlds;
    private readonly AssetCatalog _catalog;
    private readonly WorldSelection _selection;
    private readonly EngineWatcher _watcher;
    private readonly Logger _logger;

    // The reusable surface primitive (engine-view stream + shared GPU texture + interop state). Its stream
    // is bound lazily in Show() once an asset is known (the preview view needs the asset id to start).
    private readonly ViewportSurfaceViewModel _surface;

    // The previewed asset. Null until an asset is shown — either claimed from the launcher on an explicit
    // open, or resolved from the persisted layout state on a restore.
    private AssetInfo? _asset;

    // The preview world this view drives (created once the view has started) and the entities the editor
    // builds in it: the previewed asset's entity and the background sky entity.
    private World? _world;
    private Entity? _previewEntity;
    private Entity? _skyEntity;
    private bool _built;
    private bool _optionsLoaded;

    // The persisted layout state this panel records into / restores from, bound by the window manager when
    // the dock tool materializes (null until then). The restore listener waits on the asset catalog for the
    // project to finish loading; _restoreStarted guards against the repeated bind passes a re-template causes.
    private AssetViewerState? _state;
    private Action? _restoreListener;
    private bool _restoreStarted;

    // The editor's preview palette. Meshes resolve to the bridge-provided preview-mesh model assets (by
    // label); surface/sky materials resolve to the bridge-provided built-in materials (by name).
    private static readonly (string Token, string Label)[] PreviewMeshes =
    [
        ("sphere", "Sphere"), ("cube", "Cube"), ("capsule", "Capsule"),
        ("half_sphere", "Half Sphere"), ("quad", "Plane"), ("triangle", "Triangle"),
    ];
    private static readonly (string Asset, string Label)[] SurfaceMaterials =
    [
        ("PreviewMetal", "Metal"), ("PreviewMatte", "Matte"), ("PreviewUnlit", "Unlit"),
    ];
    private static readonly (string Asset, string Label)[] SkyMaterials =
    [
        ("Sky", "Day"), ("PreviewNightSky", "Night"),
    ];

    // Suppresses the re-apply while the picker is seeded to its default.
    private bool _suppressOptionPush;

    public AssetViewerViewModel(
        AssetViewerLauncher launcher, Session session, EngineRpc engine, WorldManager worlds,
        AssetCatalog catalog, WorldSelection selection, EngineWatcher watcher, Logger logger)
    {
        _session = session;
        _engine = engine;
        _worlds = worlds;
        _catalog = catalog;
        _selection = selection;
        _watcher = watcher;
        _logger = logger;

        // The surface primitive owns the stream lifecycle, the shared texture, and clearing on disconnect.
        // This view-model keeps its own richer ghost (loading vs empty vs interop) and the preview options.
        _surface = new ViewportSurfaceViewModel(session, watcher, _logger, ViewKind.AssetPreview);
        _surface.PropertyChanged += OnSurfacePropertyChanged;

        // An explicit open hands the asset through the launcher; a layout restore has none here and instead
        // reloads its remembered asset later (see BindAsset). With neither, the empty ghost shows and no
        // engine view is ever started.
        if (launcher.TakePending() is { } asset)
            Show(asset);
    }

    /// <summary>The dock-tab base title: the previewed asset's name (the remembered name while a restore is
    /// still resolving, else the generic title).</summary>
    public override string BaseTitle => _asset?.Name ?? _state?.AssetName ?? "Asset Viewer";

    /// <summary>A live preview panel: it buffers nothing and never prompts on close.</summary>
    public override bool IsLive => true;

    /// <summary>The asset viewer keeps a normal cursor (no mouselook).</summary>
    public bool WantsPointerLock => false;

    /// <summary>Orbit-only: a left-drag rotates the camera, so there is no box-select.</summary>
    public bool AllowsMarquee => false;

    /// <summary>A right-tap opens the context menu over the previewed entity.</summary>
    public bool AllowsContextMenu => true;

    /// <summary>The reusable frame surface (engine preview-camera stream + shared GPU texture). The view
    /// binds its <see cref="ViewportSurfaceViewModel.CurrentSurface"/> into the interop control.</summary>
    public ViewportSurfaceViewModel Surface => _surface;

    /// <summary>The preview-picker choices for this asset (built-in meshes, or model materials).</summary>
    public ObservableCollection<PreviewOption> PreviewOptions { get; } = [];

    /// <summary>The picker label: "Mesh" for materials/textures, "Material" for models.</summary>
    public string PreviewOptionLabel { get; private set; } = "Mesh";

    /// <summary>The selected preview option; changing it re-applies the preview.</summary>
    [ObservableProperty]
    public partial PreviewOption? SelectedPreviewOption { get; set; }

    /// <summary>The background-sky choices (day/night/none), shared by every asset type.</summary>
    public ObservableCollection<PreviewOption> SkyboxOptions { get; } = [];

    /// <summary>The selected background sky; changing it re-applies the sky.</summary>
    [ObservableProperty]
    public partial PreviewOption? SelectedSkybox { get; set; }

    /// <summary>The picker shows once the preview is on screen and there are options to choose.</summary>
    public bool ShowPreviewOptions => _surface.HasFrames && PreviewOptions.Count > 0;

    /// <summary>The background-sky picker shows only when this asset offers one. A sky material is itself
    /// the background, so it has none.</summary>
    public bool ShowSkyboxOptions => _surface.HasFrames && SkyboxOptions.Count > 0;

    /// <summary>Whether an asset is loaded or in the middle of loading (a restore awaiting the project, or a
    /// shown asset whose first frame hasn't arrived). Distinguishes the ghost's "loading" from its "empty".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GhostMessage))]
    public partial bool IsLoadingTarget { get; private set; }

    /// <summary>The empty-state ghost shows until the first frame arrives (or if interop is unavailable).</summary>
    public bool ShowEmptyGhost => !_surface.HasFrames || _surface.InteropUnavailable;

    /// <summary>The ghost's caption: a real "empty" message when nothing is loaded, a "loading" one only while
    /// an asset is actually on its way in.</summary>
    public string GhostMessage =>
        _surface.InteropUnavailable ? "GPU texture sharing is unavailable for this view."
        : IsLoadingTarget ? "Loading asset…"
        : "No asset loaded.";

    public void Dispose()
    {
        _surface.PropertyChanged -= OnSurfacePropertyChanged;
        StopListeningForRestore();
        _surface.Dispose();
    }

    // The surface gaining frames means the engine view has started (its world id is known) and the preview's
    // first frame is on screen — build the previewed entity then. Re-raise the ghost/options that derive from
    // the surface's HasFrames / InteropUnavailable.
    private void OnSurfacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Dispatch.To(DispatchContext.UI, () =>
        {
            OnPropertyChanged(nameof(ShowEmptyGhost));
            OnPropertyChanged(nameof(GhostMessage));
            OnPropertyChanged(nameof(ShowPreviewOptions));
            OnPropertyChanged(nameof(ShowSkyboxOptions));
            if (e.PropertyName == nameof(ViewportSurfaceViewModel.HasFrames) && _surface.HasFrames)
                TryBuildPreview();
        });
    }

    /// <summary>
    /// Binds the persisted layout state. On an explicit open the asset is already shown, so this records it
    /// for the layout to save; on a restore the state carries the remembered asset, which is reloaded once
    /// the project has fully loaded.
    /// </summary>
    public void BindAsset(AssetViewerState state)
    {
        _state = state;

        if (_asset is { } asset)
        {
            // Explicit open: remember what we're showing so a layout save persists it.
            state.AssetPath = asset.Path;
            state.AssetName = asset.Name;
            return;
        }

        // Layout restore: reload the remembered asset. Guarded so the repeated bind passes a re-template
        // triggers don't start a second restore.
        if (!_restoreStarted && !string.IsNullOrEmpty(state.AssetPath))
        {
            _restoreStarted = true;
            IsLoadingTarget = true;             // we're waiting on the project, not empty
            OnPropertyChanged(nameof(Title));   // surface the remembered name on the tab meanwhile
            BeginRestore(state.AssetPath);
        }
    }

    /// <summary>Forwards captured viewport input to the engine view (the engine drives the orbit camera
    /// from the deltas); cursor mapping and the relay live in the surface primitive.</summary>
    public void ForwardInput(ViewportInputPayload payload) => _surface.ForwardInput(payload);

    /// <summary>Picks the entity under a viewport click and updates the shared selection so the Inspector
    /// shows it. The point is in control space; it's letterbox-mapped to the rendered image.</summary>
    public void Tap(double pointerX, double pointerY, double controlWidth, double controlHeight, bool additive)
    {
        if (_surface.Stream is null)
            return;

        var surface = _surface.CurrentSurface;
        if (surface is null
            || !ViewportMapping.TryNormalize(
                pointerX, pointerY, controlWidth, controlHeight, surface.Width, surface.Height,
                out var u, out var v))
        {
            if (!additive)
                _selection.Clear();
            return;
        }

        PickAtAsync(u, v, additive).FireAndForget();
    }

    // Orbit views don't box-select; the marquee hooks are inert.
    public void UpdateMarquee(double x, double y, double width, double height)
    {
    }

    public void EndMarquee(
        double x, double y, double width, double height,
        double controlWidth, double controlHeight, bool additive)
    {
    }

    public void CancelMarquee()
    {
    }

    /// <summary>The preview doesn't consume Esc.</summary>
    public bool HandleEscape() => false;

    // Re-applies the previewed entity / sky when the user changes a picker (after the initial build). The
    // change handlers fire on the UI thread during seeding too, so they're suppressed until seeded + built.
    partial void OnSelectedPreviewOptionChanged(PreviewOption? value)
    {
        if (_suppressOptionPush || !_built)
            return;
        ApplyPreviewAndFrameAsync().FireAndForget();
    }

    partial void OnSelectedSkyboxChanged(PreviewOption? value)
    {
        if (_suppressOptionPush || !_built)
            return;
        ApplyBackgroundSkyAndFrameAsync().FireAndForget();
    }

    private static bool IsModelType(string type) => type is "fbx" or "obj" or "gltf" or "glb";

    private static bool IsMaterialType(string type) => type == "mat";

    // A material whose engine-declared render type is "sky": it previews as the environment background,
    // not on a mesh. The engine tags it through editor.listAssets (Asset.MaterialType).
    private static bool IsSkyMaterial(AssetInfo asset) =>
        IsMaterialType(asset.Type)
        && string.Equals(asset.MaterialType, "sky", StringComparison.OrdinalIgnoreCase);

    // Fills the pickers, resolving each choice to its engine asset handle: a sky material offers only its
    // projection shape; a model offers a built-in surface material ("Original" keeps its own); a material/
    // texture offers the built-in mesh primitive it is shown on, plus a background sky ("None" removes it).
    // Each picker is seeded to its default without a redundant re-apply.
    private async Task LoadPreviewOptionsAsync(string type)
    {
        // Ensure the catalog is populated (its first fetch also registers the built-in preview assets
        // engine-side), then resolve each choice by name/label to a handle.
        await _catalog.RefreshAsync().ContinueOnAnyContext();

        var options = new List<PreviewOption>();
        var skyOptions = new List<PreviewOption>();
        string defaultLabel;

        void AddBackgroundSkies()
        {
            foreach (var (asset, label) in SkyMaterials)
            {
                var handle = _catalog.Find(asset);
                if (!handle.IsNone)
                    skyOptions.Add(new PreviewOption(label, handle));
            }
            skyOptions.Add(new PreviewOption("None")); // AssetHandle.None → no sky
        }

        if (_asset is { } previewed && IsSkyMaterial(previewed))
        {
            // The asset is the environment itself: only its projection shape is offered (no background sky).
            options.Add(new PreviewOption("Sphere", Token: "skysphere"));
            options.Add(new PreviewOption("Box", Token: "skybox"));
            defaultLabel = "Sphere";
        }
        else if (IsModelType(type))
        {
            options.Add(new PreviewOption("Original")); // AssetHandle.None → the model's own materials
            foreach (var (asset, label) in SurfaceMaterials)
            {
                var handle = _catalog.Find(asset);
                if (!handle.IsNone)
                    options.Add(new PreviewOption(label, handle));
            }
            defaultLabel = "Original";
            AddBackgroundSkies();
        }
        else
        {
            foreach (var (_, label) in PreviewMeshes)
            {
                var handle = _catalog.Find(label); // the bridge-provided preview-mesh model assets
                if (!handle.IsNone)
                    options.Add(new PreviewOption(label, handle));
            }

            // A texture previews flat on a plane; a material previews rounded on a sphere.
            defaultLabel = IsMaterialType(type) ? "Sphere" : "Plane";
            AddBackgroundSkies();
        }

        Dispatch.To(DispatchContext.UI, () =>
        {
            foreach (var option in options)
                PreviewOptions.Add(option);
            foreach (var sky in skyOptions)
                SkyboxOptions.Add(sky);

            _suppressOptionPush = true;
            SelectedPreviewOption =
                PreviewOptions.FirstOrDefault(option => option.Label == defaultLabel)
                ?? PreviewOptions.FirstOrDefault();
            SelectedSkybox = SkyboxOptions.FirstOrDefault();
            _suppressOptionPush = false;

            OnPropertyChanged(nameof(ShowPreviewOptions));
            OnPropertyChanged(nameof(ShowSkyboxOptions));

            _optionsLoaded = true;
            TryBuildPreview();
        });
    }

    // Builds the previewed entity + background sky once both the view has started (so its world id is known)
    // and the pickers are seeded. Idempotent via _built.
    private void TryBuildPreview()
    {
        if (_built || !_optionsLoaded || _surface.Stream is not { WorldId: not 0 } || _asset is null)
            return;
        _built = true;
        BuildPreviewAsync().FireAndForget();
    }

    private async Task BuildPreviewAsync()
    {
        if (_surface.Stream is not { } stream || _asset is null)
            return;

        _world = _worlds.ForPreview(stream.WorldId, this);

        // A non-sky asset sits against the chosen background sky; a sky material is itself the background.
        if (!IsSkyMaterial(_asset))
            await EnsureSkyAsync(SelectedSkybox?.Handle ?? AssetHandle.None, SkyShape.Sphere)
                .ContinueOnAnyContext();

        await ApplyPreviewAsync().ContinueOnAnyContext();
        await stream.FrameAsync().ContinueOnAnyContext();
    }

    // (Re)builds the previewed asset's entity from the current selection, via the typed component API.
    private async Task ApplyPreviewAsync()
    {
        if (_world is null || _asset is not { } asset)
            return;

        if (IsSkyMaterial(asset))
        {
            // The asset IS the sky: project it onto the chosen shape (no separate preview entity).
            var shape = SelectedPreviewOption?.Token == "skybox" ? SkyShape.Box : SkyShape.Sphere;
            await EnsureSkyAsync(_catalog.Handle(asset.Id), shape).ContinueOnAnyContext();
            return;
        }

        var entity = await EnsurePreviewEntityAsync().ContinueOnAnyContext();
        if (entity is null)
            return;

        if (IsModelType(asset.Type))
        {
            // Show the model as-is, optionally overriding every slot with the chosen surface material.
            var surface = SelectedPreviewOption?.Handle ?? AssetHandle.None;
            await entity
                .SetComponentAsync(new Renderer
                {
                    Model = _catalog.Handle(asset.Id),
                    Materials = surface.IsNone ? [] : [surface],
                })
                .ContinueOnAnyContext();
            return;
        }

        // A material/texture shows on the chosen primitive mesh. A material binds directly; a texture binds
        // through a bridge-provided unlit material instance (a texture isn't itself a material).
        var mesh = SelectedPreviewOption?.Handle ?? AssetHandle.None;
        var material = IsMaterialType(asset.Type)
            ? _catalog.Handle(asset.Id)
            : await ResolveTextureMaterialAsync(asset.Id).ContinueOnAnyContext();

        await entity
            .SetComponentAsync(new Renderer
            {
                Model = mesh,
                Materials = material.IsNone ? [] : [material],
            })
            .ContinueOnAnyContext();
    }

    private async Task<AssetHandle> ResolveTextureMaterialAsync(long textureId)
    {
        var result = await _engine
            .PreviewTextureMaterialAsync(textureId, CancellationToken.None).ContinueOnAnyContext();
        if (result.Success)
            return AssetHandle.FromId(result.Value);

        _logger.Error($"Asset viewer: couldn't build a preview material for the texture: {result.Error}");
        return AssetHandle.None;
    }

    private async Task<Entity?> EnsurePreviewEntityAsync()
    {
        if (_previewEntity is not null || _world is null)
            return _previewEntity;

        var created = await _world.CreateEntityAsync("Preview", parent: 0UL, CancellationToken.None)
            .ContinueOnAnyContext();
        if (!created.Success)
        {
            _logger.Error($"Asset viewer: couldn't create the preview entity: {created.Error}");
            return null;
        }

        _previewEntity = created.Value;
        return _previewEntity;
    }

    // Points the background sky at the given material (creating the sky entity on first use), or removes it
    // when the handle is None.
    private async Task EnsureSkyAsync(AssetHandle sky, SkyShape shape)
    {
        if (_world is null)
            return;

        if (sky.IsNone)
        {
            if (_skyEntity is { } existing)
            {
                await existing.DestroyAsync(CancellationToken.None).ContinueOnAnyContext();
                _skyEntity = null;
            }
            return;
        }

        var skyEntity = _skyEntity;
        if (skyEntity is null)
        {
            var created = await _world.CreateEntityAsync("Sky", parent: 0UL, CancellationToken.None)
                .ContinueOnAnyContext();
            if (!created.Success)
            {
                _logger.Error($"Asset viewer: couldn't create the sky entity: {created.Error}");
                return;
            }
            skyEntity = created.Value;
            if (skyEntity is null)
                return;
            _skyEntity = skyEntity;
        }

        await skyEntity
            .SetComponentAsync(new Sky { Material = new MaterialInstance(sky), Shape = shape })
            .ContinueOnAnyContext();
    }

    private async Task ApplyPreviewAndFrameAsync()
    {
        await ApplyPreviewAsync().ContinueOnAnyContext();
        if (_surface.Stream is { } stream)
            await stream.FrameAsync().ContinueOnAnyContext();
    }

    private async Task ApplyBackgroundSkyAndFrameAsync()
    {
        await EnsureSkyAsync(SelectedSkybox?.Handle ?? AssetHandle.None, SkyShape.Sphere)
            .ContinueOnAnyContext();
        if (_surface.Stream is { } stream)
            await stream.FrameAsync().ContinueOnAnyContext();
    }

    /// <inheritdoc/>
    public async Task<ulong?> PickAndSelectForMenuAsync(
        double x, double y, double controlWidth, double controlHeight)
    {
        if (_surface.Stream is not { } stream)
            return null;

        var surface = _surface.CurrentSurface;
        if (surface is null
            || !ViewportMapping.TryNormalize(
                x, y, controlWidth, controlHeight, surface.Width, surface.Height, out var u, out var v))
            return null;

        // Stay on the UI thread so the selection is set before the caller builds the menu against it.
        var result = await stream.PickAsync(u, v).ContinueOnSameContext();
        if (!result.Success || result.Value is not { } hit)
            return null;

        _selection.Set(hit);
        return hit;
    }

    private async Task PickAtAsync(double u, double v, bool additive)
    {
        if (_surface.Stream is not { } stream)
            return;

        var result = await stream.PickAsync(u, v).ContinueOnAnyContext();
        if (!result.Success)
            return;

        var id = result.Value;
        Dispatch.To(DispatchContext.UI, () =>
        {
            if (id is { } hit)
            {
                if (additive)
                    _selection.Toggle(hit);
                else
                    _selection.Set(hit);
            }
            else if (!additive)
            {
                _selection.Clear();
            }
        });
    }

    // Starts previewing an asset: opens its engine view (its own preview world + orbit camera) and seeds the
    // preview pickers. Called for an explicit open and again when a restore resolves its remembered asset.
    // The previewed entity is built once the surface gains frames (see OnSurfacePropertyChanged), by which
    // point the view's world id is known.
    private void Show(AssetInfo asset)
    {
        _asset = asset;
        IsLoadingTarget = true;             // a frame hasn't arrived yet → the ghost reads "loading"
        OnPropertyChanged(nameof(Title));   // BaseTitle now resolves to the asset's name

        _surface.Prepare(new ViewportStream(_session, _engine, ViewKind.AssetPreview, asset.Id));

        // The preview picker: a sky material picks only its projection shape; a model picks a surface
        // material; a material/texture picks the mesh it shows on.
        PreviewOptionLabel =
            IsSkyMaterial(asset) ? "Shape"
            : IsModelType(asset.Type) ? "Material"
            : "Mesh";
        OnPropertyChanged(nameof(PreviewOptionLabel));
        LoadPreviewOptionsAsync(asset.Type).FireAndForget();

        // Record the now-shown asset so a layout save persists it (no-op on a restore that's reloading the
        // same path it came from).
        if (_state is { } state)
        {
            state.AssetPath = asset.Path;
            state.AssetName = asset.Name;
        }
    }

    // Reloads a remembered asset (by its stable project-relative path) once the project has fully loaded —
    // i.e. the engine is connected and its asset catalog is populated. Re-checks on every catalog change so
    // the initial connect (or a reconnect) is handled. If the asset is gone, logs an error and leaves the
    // empty ghost rather than starting a dead view.
    private void BeginRestore(string assetPath)
    {
        void TryResolve()
        {
            if (_asset is not null)
                return; // already resolved (a duplicate catalog event)

            // Not fully loaded yet — the catalog is empty until the engine connects and lists its assets
            // (which always include the built-in preview palette). Keep waiting.
            if (_session.State != ConnectionState.Connected || _catalog.Assets.Count == 0)
                return;

            StopListeningForRestore();

            var match = _catalog.Assets.FirstOrDefault(
                candidate => string.Equals(candidate.Path, assetPath, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                _logger.Error(
                    $"Asset viewer: the previously open asset '{assetPath}' could not be found; it may have "
                    + "been moved or deleted. Showing the empty viewer.");
                IsLoadingTarget = false; // nothing to load → the ghost reads "empty"
                return;
            }

            Show(match);
        }

        // The catalog raises Changed off the UI thread; resolve (and start the engine view) back on it.
        _restoreListener = () => Dispatch.To(DispatchContext.UI, TryResolve);
        _catalog.Changed += _restoreListener;

        // Cover the already-loaded case (the project was up before this panel was restored).
        Dispatch.To(DispatchContext.UI, TryResolve);
    }

    private void StopListeningForRestore()
    {
        if (_restoreListener is { } listener)
        {
            _catalog.Changed -= listener;
            _restoreListener = null;
        }
    }
}
