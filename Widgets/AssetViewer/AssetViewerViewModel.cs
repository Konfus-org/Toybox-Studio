using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services.AssetViewing;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Services.World;
using Toybox.Studio.Shell.Panels;
using Toybox.Studio.Shell.Workspace;
using Toybox.Studio.Utils;
using Toybox.Studio.Widgets.Viewport;

namespace Toybox.Studio.Widgets.AssetViewer;

/// <summary>
/// A non-singleton preview panel: shows one asset (a texture on a plane, a material on a sphere, or a
/// model) loaded into its own isolated engine world, driven by an orbit camera. It reuses the viewport's
/// GPU-texture surface (<see cref="ViewportStream"/> + the interop control) and input behavior; the
/// engine interprets the same input deltas as orbit/zoom for an asset-preview view. Clicking an entity
/// updates the shared <see cref="WorldSelection"/> so the existing Inspector shows it.
/// </summary>
public sealed partial class AssetViewerViewModel : DataPanel, IDisposable, IViewportInputSink, IAssetViewerHost
{
    private readonly Session _session;
    private readonly EngineRpc _engine;
    private readonly AssetCatalog _catalog;
    private readonly WorldSelection _selection;
    private readonly Logger _logger;
    private readonly Action<ConnectionState> _onStateChanged;

    // The previewed asset and its engine view. Both are null until an asset is shown — either claimed from
    // the launcher on an explicit open, or resolved from the persisted layout state on a restore.
    private ViewportStream? _stream;
    private Asset? _asset;

    // The persisted layout state this panel records into / restores from, bound by the window manager when
    // the dock tool materializes (null until then). The restore listener waits on the asset catalog for the
    // project to finish loading; _restoreStarted guards against the repeated bind passes a re-template causes.
    private AssetViewerState? _state;
    private Action? _restoreListener;
    private bool _restoreStarted;

    // The editor owns the preview palette's labels; it looks up the engine-provided built-in assets by
    // name (they arrive flagged as built-in through editor.listAssets) to get their ids. Built-in meshes
    // are engine primitives (not assets), so the editor names those tokens directly.
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

    // Suppresses the rebuild RPC while the picker is seeded to its default in the constructor.
    private bool _suppressOptionPush;

    public AssetViewerViewModel(
        AssetViewerLauncher launcher, Session session, EngineRpc engine, AssetCatalog catalog,
        WorldSelection selection, Logger logger)
    {
        _session = session;
        _engine = engine;
        _catalog = catalog;
        _selection = selection;
        _logger = logger;

        _onStateChanged = state => Dispatch.To(DispatchContext.UI, () =>
        {
            if (state != ConnectionState.Connected)
                ClearSurface();
        });
        session.StateChanged += _onStateChanged;

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

    /// <summary>Never the game view.</summary>
    public bool IsGame => false;

    /// <summary>The asset viewer keeps a normal cursor (no mouselook).</summary>
    public bool RelativeMouse => false;

    /// <summary>Orbit-only: a left-drag rotates the camera, so there is no box-select.</summary>
    public bool MarqueeEnabled => false;

    /// <summary>The engine view's shared GPU texture, bound by the view into the interop control.</summary>
    [ObservableProperty]
    public partial ViewSurface? CurrentSurface { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyGhost))]
    [NotifyPropertyChangedFor(nameof(ShowPreviewOptions))]
    [NotifyPropertyChangedFor(nameof(ShowSkyboxOptions))]
    public partial bool HasFrames { get; private set; }

    /// <summary>The preview-picker choices for this asset (built-in meshes, or model materials).</summary>
    public ObservableCollection<PreviewOption> PreviewOptions { get; } = [];

    /// <summary>The picker label: "Mesh" for materials/textures, "Material" for models.</summary>
    public string PreviewOptionLabel { get; private set; } = "Mesh";

    /// <summary>The selected preview option; changing it rebuilds the preview engine-side.</summary>
    [ObservableProperty]
    public partial PreviewOption? SelectedPreviewOption { get; set; }

    /// <summary>The background-sky choices (day/night/none), shared by every asset type.</summary>
    public ObservableCollection<PreviewOption> SkyboxOptions { get; } = [];

    /// <summary>The selected background sky; changing it rebuilds the preview engine-side.</summary>
    [ObservableProperty]
    public partial PreviewOption? SelectedSkybox { get; set; }

    /// <summary>The picker shows once the preview is on screen and there are options to choose.</summary>
    public bool ShowPreviewOptions => HasFrames && PreviewOptions.Count > 0;

    /// <summary>The background-sky picker shows only when this asset offers one. A sky material is itself
    /// the background, so it has none.</summary>
    public bool ShowSkyboxOptions => HasFrames && SkyboxOptions.Count > 0;

    /// <summary>Set by the interop control when this compositor can't import shared GPU textures.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyGhost))]
    [NotifyPropertyChangedFor(nameof(GhostMessage))]
    public partial bool InteropUnavailable { get; set; }

    /// <summary>Whether an asset is loaded or in the middle of loading (a restore awaiting the project, or a
    /// shown asset whose first frame hasn't arrived). Distinguishes the ghost's "loading" from its "empty".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GhostMessage))]
    public partial bool IsLoadingTarget { get; private set; }

    /// <summary>The empty-state ghost shows until the first frame arrives (or if interop is unavailable).</summary>
    public bool ShowEmptyGhost => !HasFrames || InteropUnavailable;

    /// <summary>The ghost's caption: a real "empty" message when nothing is loaded, a "loading" one only while
    /// an asset is actually on its way in.</summary>
    public string GhostMessage =>
        InteropUnavailable ? "GPU texture sharing is unavailable for this view."
        : IsLoadingTarget ? "Loading asset…"
        : "No asset loaded.";

    public void Dispose()
    {
        _session.StateChanged -= _onStateChanged;
        StopListeningForRestore();
        if (_stream is not null)
        {
            _stream.SurfaceArrived -= OnSurfaceArrived;
            _stream.SurfaceLost -= OnSurfaceLost;
            _stream.Dispose();
        }
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

    /// <summary>Forwards captured viewport input to the engine view, mapping the cursor to normalized
    /// image coordinates (the engine drives the orbit camera from the deltas).</summary>
    public void ForwardInput(ViewportInputPayload payload)
    {
        if (_stream is null)
            return;

        var cursorU = 0.0;
        var cursorV = 0.0;
        if (CurrentSurface is { } surface)
            (cursorU, cursorV) = ViewportMapping.NormalizeClamped(
                payload.MouseX, payload.MouseY, payload.ControlWidth, payload.ControlHeight,
                surface.Width, surface.Height);

        _stream.SendInput(
            payload.Focused, payload.Buttons, payload.MoveKeys, payload.Keys,
            payload.MouseX, payload.MouseY, payload.Dx, payload.Dy, payload.Wheel, cursorU, cursorV);
    }

    /// <summary>Picks the entity under a viewport click and updates the shared selection so the Inspector
    /// shows it. The point is in control space; it's letterbox-mapped to the rendered image.</summary>
    public void Pick(double pointerX, double pointerY, double controlWidth, double controlHeight, bool additive)
    {
        if (_stream is null)
            return;

        var surface = CurrentSurface;
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

    public void StopGame()
    {
    }

    // Sends the chosen presentation to the engine, which rebuilds the preview in place (the camera and
    // orbit are preserved): a built-in mesh token (or "skybox"/"skysphere") for a material/texture, or
    // a built-in surface material id for a model. Suppressed while the picker is seeded to its default.
    partial void OnSelectedPreviewOptionChanged(PreviewOption? value)
    {
        if (_suppressOptionPush || _stream is null || value is null)
            return;
        _stream.SetPreviewOption(value.Token, value.Id);
    }

    partial void OnSelectedSkyboxChanged(PreviewOption? value)
    {
        if (_suppressOptionPush || _stream is null || value is null)
            return;
        _stream.SetPreviewSkybox(value.Id);
    }

    private static bool IsModelType(string type) => type is "fbx" or "obj" or "gltf" or "glb";

    private static bool IsMaterialType(string type) => type == "mat";

    // A material whose engine-declared render type is "sky": it previews as the environment background,
    // not on a mesh. The engine tags it through editor.listAssets (Asset.MaterialType).
    private static bool IsSkyMaterial(Asset asset) =>
        IsMaterialType(asset.Type)
        && string.Equals(asset.MaterialType, "sky", StringComparison.OrdinalIgnoreCase);

    // Fills the pickers from the engine's built-in assets (looked up by name from the catalog, which the
    // engine surfaces through editor.listAssets) plus the built-in mesh primitives. A sky material picks
    // only its projection shape (it loads straight into the background, so no mesh/material/background
    // pickers); a model picks a built-in surface material ("Original" keeps its own); a material/texture
    // picks the built-in mesh it is shown on, plus a background sky ("None" removes it). Each picker is
    // seeded to its default without a redundant rebuild.
    private async Task LoadPreviewOptionsAsync(string type)
    {
        // Ensure the catalog is populated (its first fetch is also what registers the built-in assets
        // engine-side), then resolve each built-in by name to its id.
        await _catalog.RefreshAsync().ContinueOnAnyContext();
        var builtins = _catalog.Assets.Where(asset => asset.IsBuiltin).ToList();
        long IdOf(string name) =>
            builtins.FirstOrDefault(asset => asset.Name == name)?.Id ?? 0;

        var options = new List<PreviewOption>();
        var skyOptions = new List<PreviewOption>();
        string defaultToken;

        // The background-sky choices (day/night/none) every non-sky asset can sit against.
        void AddBackgroundSkies()
        {
            foreach (var (asset, label) in SkyMaterials)
            {
                var id = IdOf(asset);
                if (id != 0)
                    skyOptions.Add(new PreviewOption(label, Id: id));
            }
            skyOptions.Add(new PreviewOption("None")); // id 0 → no sky
        }

        if (_asset is { } previewed && IsSkyMaterial(previewed))
        {
            // A sky material is the environment itself: only its projection shape is offered, and there
            // is no background-sky picker (it would replace the very material being previewed). The
            // engine defaults a sky material to the sphere projection, so seed that without a rebuild.
            options.Add(new PreviewOption("Sphere", "skysphere"));
            options.Add(new PreviewOption("Box", "skybox"));
            defaultToken = "skysphere";
        }
        else if (IsModelType(type))
        {
            options.Add(new PreviewOption("Original"));
            foreach (var (asset, label) in SurfaceMaterials)
            {
                var id = IdOf(asset);
                if (id != 0)
                    options.Add(new PreviewOption(label, Id: id));
            }
            defaultToken = string.Empty; // the "Original" entry
            AddBackgroundSkies();
        }
        else
        {
            foreach (var (token, label) in PreviewMeshes)
                options.Add(new PreviewOption(label, token));

            // A texture previews flat on a plane; a material previews rounded on a sphere — match the
            // engine's default mesh so seeding the picker doesn't trigger a rebuild.
            defaultToken = IsMaterialType(type) ? "sphere" : "quad";
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
                PreviewOptions.FirstOrDefault(option => option.Token == defaultToken)
                ?? PreviewOptions.FirstOrDefault();
            SelectedSkybox = SkyboxOptions.FirstOrDefault();
            _suppressOptionPush = false;

            OnPropertyChanged(nameof(ShowPreviewOptions));
            OnPropertyChanged(nameof(ShowSkyboxOptions));
        });
    }

    private async Task PickAtAsync(double u, double v, bool additive)
    {
        var result = await _stream!.PickAsync(u, v).ContinueOnAnyContext();
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

    private void OnSurfaceArrived(ViewSurface surface) =>
        Dispatch.To(DispatchContext.UI, () => ApplySurface(surface));

    private void OnSurfaceLost() =>
        Dispatch.To(DispatchContext.UI, ClearSurface);

    private void ApplySurface(ViewSurface surface)
    {
        if (surface.Handle == 0)
        {
            _logger.Warning(
                "GPU texture sharing is unavailable for this view; the asset viewer will stay empty. "
                + "See the engine log for the driver/adapter reason.");
            ClearSurface();
            return;
        }

        CurrentSurface = surface;
        HasFrames = true;
    }

    private void ClearSurface()
    {
        CurrentSurface = null;
        HasFrames = false;
    }

    // Starts previewing an asset: opens its engine view (its own preview world + orbit camera) and populates
    // the preview pickers. Called for an explicit open and again when a restore resolves its remembered asset.
    private void Show(Asset asset)
    {
        _asset = asset;
        IsLoadingTarget = true;             // a frame hasn't arrived yet → the ghost reads "loading"
        OnPropertyChanged(nameof(Title));   // BaseTitle now resolves to the asset's name

        _stream = new ViewportStream(_session, _engine, ViewKind.AssetPreview, asset.Id);
        _stream.SurfaceArrived += OnSurfaceArrived;
        _stream.SurfaceLost += OnSurfaceLost;

        // The preview picker: a sky material picks only its projection shape; a model picks a surface
        // material; a material/texture picks the mesh it shows on. Most also pick a background sky. The
        // material/sky choices are the engine's built-in assets (sourced from editor.listAssets); meshes/
        // shapes are engine primitives.
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
