using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Input;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.Viewport;

/// <summary>
/// The one viewport control. It displays an engine view by sampling its shared GPU texture directly
/// through Avalonia's composition GPU interop — no CPU readback, no per-frame upload: the engine
/// renders into a D3D11 keyed-mutex texture; this control imports that texture by its global shared
/// handle and hands it to a <see cref="CompositionDrawingSurface"/> each compositor frame (producer
/// owns key 1; this consumer acquires 1 / releases 0). It is also the viewport's input surface — the
/// input-binding behavior forwards captured snapshots here, and the cover-scaling mapping (how the
/// texture fills the control, and so where the pointer lands on it) lives here too, view concerns
/// both — before they flow on to the bound <see cref="ViewportViewModel"/>.
/// </summary>
[Dockable(Title = "Viewport", Icon = "Axis3d", Slot = DockSlot.Top, Singleton = false,
    FloatWidth = 960, FloatHeight = 600)]
public partial class ViewportView : UserControl, IInputSink
{
    /// <summary>The engine view's shared GPU texture to show (bound to the view-model). Null clears.</summary>
    public static readonly StyledProperty<ViewSurfaceCreated?> SurfaceProperty =
        AvaloniaProperty.Register<ViewportView, ViewSurfaceCreated?>(nameof(Surface));

    /// <summary>
    /// Set true (one-way to the view-model) when this compositor can't import shared GPU textures,
    /// so the view-model can show its empty ghost instead of a silent black viewport.
    /// </summary>
    public static readonly StyledProperty<bool> InteropUnavailableProperty =
        AvaloniaProperty.Register<ViewportView, bool>(nameof(InteropUnavailable));

    private Compositor? _compositor;
    private CompositionDrawingSurface? _drawingSurface;
    private CompositionSurfaceVisual? _visual;
    private ICompositionGpuInterop? _interop;
    private ICompositionImportedGpuImage? _imported;
    private Action? _update;
    private Task? _lastPresent;
    private bool _initialized;
    private bool _running;
    // Serializes ImportCurrentSurfaceAsync so two SurfaceProperty changes can't race _imported/_lastPresent
    // (double-dispose or leak). While an import runs, a newer request just sets _importPending; the running
    // import re-loops when it finishes so the latest Surface always wins. UI-thread-only, so no lock needed.
    private bool _importing;
    private bool _importPending;
    // Bumped on every TearDown. The import driver captures it and, after each await, bails if it no longer
    // matches — so a driver left suspended across a detach/re-attach never touches the new generation's state.
    private int _generation;

    public ViewportView()
    {
        InitializeComponent();
        // The capture lives on the dedicated InputSurface (a sibling BELOW the toolbar overlay, so
        // toolbar clicks never route through it); this control stays the sink — snapshots arrive in
        // ForwardInput below. InputSurface fills the same panel, so snapshot bounds match this control.
        InputBindingBehavior.SetSink(InputSurface, this);
    }

    public ViewSurfaceCreated? Surface
    {
        get => GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    public bool InteropUnavailable
    {
        get => GetValue(InteropUnavailableProperty);
        set => SetValue(InteropUnavailableProperty, value);
    }

    /// <summary>
    /// The input-binding behavior's sink: maps the pointer into the displayed image (the same cover
    /// mapping the display uses, so what is shown and what is pointed at can never drift apart), then
    /// hands the snapshot to the view-model to stream to the engine view.
    /// </summary>
    public void ForwardInput(InputSnapshot input)
    {
        if (Surface is { } surface)
        {
            var (u, v) = NormalizeClamped(
                input.PointerPosition.X, input.PointerPosition.Y,
                input.ControlSize.Width, input.ControlSize.Height,
                surface.Width, surface.Height);
            input = input with { NormalizedPointer = new Point(u, v) };
        }

        (DataContext as ViewportViewModel)?.ForwardInput(input);
    }

    // Initialization waits for Loaded rather than OnAttachedToVisualTree: SurfaceHost's composition
    // visual does not exist yet while the attach walk is still running, so querying it there reads as
    // "compositor unavailable" and permanently blacks out a perfectly capable viewport.
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        InitializeAsync().FireAndForget();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        TearDown();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SurfaceProperty && _initialized)
            ImportCurrentSurfaceAsync().FireAndForget();
        else if (change.Property == BoundsProperty)
            UpdateVisualLayout();
    }

    private async Task InitializeAsync()
    {
        if (_initialized)
            return;

        // The composition visual lives on the SurfaceHost child so the ghost overlays (later siblings)
        // always draw above the streamed image.
        var selfVisual = ElementComposition.GetElementVisual(SurfaceHost);
        if (selfVisual is null)
        {
            InteropUnavailable = true;
            return;
        }

        _compositor = selfVisual.Compositor;
        _drawingSurface = _compositor.CreateDrawingSurface();
        _visual = _compositor.CreateSurfaceVisual();
        _visual.Surface = _drawingSurface;
        ElementComposition.SetElementChildVisual(SurfaceHost, _visual);
        UpdateVisualLayout();

        var compositor = _compositor;
        var interop = await compositor.TryGetCompositionGpuInterop().ContinueOnSameContext();

        // A detach (TearDown nulls _compositor) may have raced this await. If we were torn down, drop the
        // interop we just obtained and bail — otherwise we'd light up a half-built viewport that the
        // matching detach already cleaned up, leaving it permanently black.
        if (_compositor is null || !ReferenceEquals(_compositor, compositor))
            return;

        _interop = interop;
        if (_interop is null
            || !_interop.SupportedImageHandleTypes.Contains(
                KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle))
        {
            // The view-model logs the reason and shows the ghost.
            InteropUnavailable = true;
            return;
        }

        InteropUnavailable = false;
        _initialized = true;
        _update ??= UpdateFrame;
        // Resume on the UI thread: StartLoop and the composition objects it drives are UI-thread-affine.
        await ImportCurrentSurfaceAsync().ContinueOnSameContext();

        // Re-check after the import await: a detach during it tears everything down, and starting the
        // compositor loop afterwards would resurrect a dead viewport.
        if (_compositor is null || _interop is null)
            return;

        StartLoop();
    }

    private void StartLoop()
    {
        if (_running)
            return;
        _running = true;
        _compositor?.RequestCompositionUpdate(_update!);
    }

    private void UpdateFrame()
    {
        if (!_running)
            return;

        // Present a new frame only when the previous present has completed, so a slow producer never
        // piles up acquisitions on the compositor's render thread; the surface keeps its last content
        // in the meantime.
        if (_imported is not null && _drawingSurface is not null
            && (_lastPresent is null || _lastPresent.IsCompleted))
        {
            _lastPresent = _drawingSurface.UpdateWithKeyedMutexAsync(_imported, 1, 0);
        }

        _compositor?.RequestCompositionUpdate(_update!);
    }

    private async Task ImportCurrentSurfaceAsync()
    {
        // Only one import may run at a time. A request arriving mid-import is coalesced into a single
        // pending re-run that picks up whatever the latest Surface is, so the newest surface wins without
        // overlapping imports racing _imported/_lastPresent.
        if (_importing)
        {
            _importPending = true;
            return;
        }

        var generation = _generation;
        _importing = true;
        try
        {
            do
            {
                _importPending = false;
                await ImportSurfaceCoreAsync().ContinueOnSameContext();
                // A detach during the import bumps _generation; this driver is now stale, so stop before it
                // can clear the live generation's _importing guard below.
                if (generation != _generation)
                    return;
            }
            while (_importPending && _interop is not null);
        }
        finally
        {
            if (generation == _generation)
                _importing = false;
        }
    }

    private async Task ImportSurfaceCoreAsync()
    {
        // Drop the previous import only once any in-flight present that referenced it has finished.
        var old = _imported;
        _imported = null;
        if (old is not null)
        {
            if (_lastPresent is not null)
            {
                try
                {
                    // Stay on the UI thread across the wait: the import/dispose and the visual touch
                    // below are UI-thread-affine composition calls.
                    await _lastPresent.ContinueOnSameContext();
                }
                catch
                {
                    // A failed present must not block teardown of the old image.
                }
            }

            await old.DisposeAsync().ContinueOnSameContext();
        }

        if (_interop is null || Surface is not { } surface || surface.Handle == 0)
            return;

        var properties = new PlatformGraphicsExternalImageProperties
        {
            Width = surface.Width,
            Height = surface.Height,
            Format = PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm,
            // The engine renders with OpenGL's bottom-left origin, so the shared texture is bottom-up.
            TopLeftOrigin = false,
        };
        var handle = new PlatformHandle(
            new IntPtr(surface.Handle),
            KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle);
        _imported = _interop.ImportImage(handle, properties);
        UpdateVisualLayout();
    }

    /// <summary>Covers the host with the shared texture preserving its aspect ratio (see
    /// <see cref="ImageRect"/>), centred and clipped on the long axis.</summary>
    private void UpdateVisualLayout()
    {
        if (_visual is null)
            return;

        var bounds = SurfaceHost.Bounds;
        if (Surface is not { Width: > 0, Height: > 0 } surface
            || bounds.Width <= 0 || bounds.Height <= 0)
        {
            _visual.Size = new Vector2((float)bounds.Width, (float)bounds.Height);
            _visual.Offset = new Vector3(0F, 0F, 0F);
            return;
        }

        var (offsetX, offsetY, width, height) =
            ImageRect(bounds.Width, bounds.Height, surface.Width, surface.Height);
        _visual.Size = new Vector2((float)width, (float)height);
        _visual.Offset = new Vector3((float)offsetX, (float)offsetY, 0F);
    }

    private void TearDown()
    {
        _running = false;
        _initialized = false;
        // Bump the generation and clear the import guard so a re-attach's fresh import isn't blocked by a
        // guard left set by an import that was in flight when we tore down (a stale driver checks the
        // generation and won't touch the new one's state; its core body also bails on the nulled _interop).
        _generation++;
        _importing = false;
        _importPending = false;

        var imported = _imported;
        _imported = null;
        var lastPresent = _lastPresent;
        _lastPresent = null;

        DisposeImportedAsync(imported, lastPresent).FireAndForget();

        if (_visual is not null)
            _visual.Surface = null;
        _drawingSurface?.Dispose();
        _drawingSurface = null;
        _visual = null;
        _interop = null;
        _compositor = null;
    }

    private static async Task DisposeImportedAsync(ICompositionImportedGpuImage? imported, Task? lastPresent)
    {
        if (imported is null)
            return;

        if (lastPresent is not null)
        {
            try
            {
                // Released on the UI thread (TearDown's caller): the imported image is UI-thread-affine.
                await lastPresent.ContinueOnSameContext();
            }
            catch
            {
                // Ignore a failed present; we still need to release the image.
            }
        }

        await imported.DisposeAsync().ContinueOnSameContext();
    }

    // --- The Stretch=UniformToFill (cover / centre-crop) mapping, shared by the display and the
    // pointer normalization above so what is shown and what is pointed at can never drift apart. ---

    /// <summary>
    /// The destination rect (offset + size) the surface occupies inside the host: the smallest centred
    /// rect that covers it while preserving the surface's aspect ratio, overflowing on the long axis
    /// (the host clips). Falls back to the whole host when a size is degenerate.
    /// </summary>
    private static (double X, double Y, double Width, double Height) ImageRect(
        double controlW, double controlH, double surfW, double surfH)
    {
        if (controlW <= 0 || controlH <= 0 || surfW <= 0 || surfH <= 0)
            return (0, 0, controlW, controlH);

        var scale = Math.Max(controlW / surfW, controlH / surfH);
        var width = surfW * scale;
        var height = surfH * scale;
        return ((controlW - width) / 2, (controlH - height) / 2, width, height);
    }

    /// <summary>
    /// Maps a control-space point to normalized image coordinates, clamped to [0,1] (so a point past
    /// the control edge folds onto the nearest image edge).
    /// </summary>
    private static (double U, double V) NormalizeClamped(
        double px, double py, double controlW, double controlH, double surfW, double surfH)
    {
        var (x, y, width, height) = ImageRect(controlW, controlH, surfW, surfH);
        if (width <= 0 || height <= 0)
            return (0, 0);

        return (Math.Clamp((px - x) / width, 0, 1), Math.Clamp((py - y) / height, 0, 1));
    }
}
