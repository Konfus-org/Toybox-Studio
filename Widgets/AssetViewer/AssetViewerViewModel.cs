using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Services.World;
using Toybox.Studio.Shell.Panels;
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
public sealed partial class AssetViewerViewModel : DataPanel, IDisposable, IViewportInputSink
{
    private readonly ViewportStream? _stream;
    private readonly Session _session;
    private readonly WorldSelection _selection;
    private readonly Logger _logger;
    private readonly Asset? _asset;
    private readonly Action<ConnectionState> _onStateChanged;

    // Suppresses the rebuild RPC while the picker is seeded to its default in the constructor.
    private bool _suppressOptionPush;

    public AssetViewerViewModel(
        AssetViewerLauncher launcher, Session session, EngineRpc engine, WorldSelection selection,
        Logger logger)
    {
        _session = session;
        _selection = selection;
        _logger = logger;
        _asset = launcher.TakePending();

        // No target asset (e.g. the panel was rematerialized by a layout restore): show the empty
        // ghost and never start an engine view.
        if (_asset is { } asset)
        {
            _stream = new ViewportStream(session, engine, ViewKind.AssetPreview, asset.Id);
            _stream.SurfaceArrived += OnSurfaceArrived;
            _stream.SurfaceLost += OnSurfaceLost;

            // The preview picker: a mesh choice for materials/textures, a material choice for models,
            // plus a background-sky choice for all of them.
            var (label, options) = OptionsFor(asset.Type);
            PreviewOptionLabel = label;
            foreach (var option in options)
                PreviewOptions.Add(option);
            foreach (var sky in SkyboxChoices)
                SkyboxOptions.Add(sky);

            // Seed both pickers to the engine's defaults (first option) without a redundant rebuild.
            _suppressOptionPush = true;
            SelectedPreviewOption = PreviewOptions.FirstOrDefault();
            SelectedSkybox = SkyboxOptions.FirstOrDefault();
            _suppressOptionPush = false;
        }

        _onStateChanged = state => Dispatch.To(DispatchContext.UI, () =>
        {
            if (state != ConnectionState.Connected)
                ClearSurface();
        });
        session.StateChanged += _onStateChanged;
    }

    /// <summary>The dock-tab base title: the previewed asset's name.</summary>
    public override string BaseTitle => _asset?.Name ?? "Asset Viewer";

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

    // The background-sky options, the same for every asset type. "Day" is the engine default.
    private static readonly PreviewOption[] SkyboxChoices =
    [
        new PreviewOption("Day", "day"),
        new PreviewOption("Night", "night"),
        new PreviewOption("None", "none"),
    ];

    /// <summary>Set by the interop control when this compositor can't import shared GPU textures.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyGhost))]
    public partial bool InteropUnavailable { get; set; }

    /// <summary>The empty-state ghost shows until the first frame arrives (or if interop is unavailable).</summary>
    public bool ShowEmptyGhost => !HasFrames || InteropUnavailable;

    public void Dispose()
    {
        _session.StateChanged -= _onStateChanged;
        if (_stream is not null)
        {
            _stream.SurfaceArrived -= OnSurfaceArrived;
            _stream.SurfaceLost -= OnSurfaceLost;
            _stream.Dispose();
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

    // Sends the chosen mesh/material to the engine, which rebuilds the preview in place (the camera and
    // orbit are preserved). Suppressed while the picker is seeded to its default in the constructor.
    partial void OnSelectedPreviewOptionChanged(PreviewOption? value)
    {
        if (_suppressOptionPush || _stream is null || value is null)
            return;
        _stream.SetPreviewOption(value.Token);
    }

    partial void OnSelectedSkyboxChanged(PreviewOption? value)
    {
        if (_suppressOptionPush || _stream is null || value is null)
            return;
        _stream.SetPreviewSkybox(value.Token);
    }

    // The preview-picker label and choices for an asset type. The first entry is the engine's default,
    // so seeding the picker to it matches what the view already shows. Models pick a built-in material;
    // materials and textures pick the built-in mesh the asset is shown on (materials add sky options).
    private static (string Label, IReadOnlyList<PreviewOption> Options) OptionsFor(string type)
    {
        if (type is "fbx" or "obj" or "gltf" or "glb")
            return ("Material",
            [
                new PreviewOption("Metal", "metal"),
                new PreviewOption("Matte", "matte"),
                new PreviewOption("Unlit", "unlit"),
                new PreviewOption("Original", "original"),
            ]);

        if (type == "mat")
            return ("Mesh",
            [
                new PreviewOption("Sphere", "sphere"),
                new PreviewOption("Cube", "cube"),
                new PreviewOption("Capsule", "capsule"),
                new PreviewOption("Half Sphere", "half_sphere"),
                new PreviewOption("Plane", "quad"),
                new PreviewOption("Triangle", "triangle"),
                new PreviewOption("Skybox", "skybox"),
                new PreviewOption("Sky Sphere", "skysphere"),
            ]);

        // Textures.
        return ("Mesh",
        [
            new PreviewOption("Plane", "quad"),
            new PreviewOption("Sphere", "sphere"),
            new PreviewOption("Cube", "cube"),
            new PreviewOption("Capsule", "capsule"),
            new PreviewOption("Half Sphere", "half_sphere"),
            new PreviewOption("Triangle", "triangle"),
        ]);
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
}
