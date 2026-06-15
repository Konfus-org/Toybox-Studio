using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// Displays an engine view by sampling its shared GPU texture directly through Avalonia's
/// composition GPU interop — no CPU readback, no per-frame upload. The engine renders into a
/// D3D11 keyed-mutex texture; this control imports that texture by its global shared handle and
/// hands it to a <see cref="CompositionDrawingSurface"/> each compositor frame. The producer
/// (engine) owns keyed-mutex key 1; this consumer acquires 1 / releases 0, matching the engine's
/// acquire 0 / release 1.
/// </summary>
public sealed class CompositionInteropViewport : Control
{
    /// <summary>The engine view's shared GPU texture to show. Null clears the display.</summary>
    public static readonly StyledProperty<ViewSurface?> SurfaceProperty =
        AvaloniaProperty.Register<CompositionInteropViewport, ViewSurface?>(nameof(Surface));

    /// <summary>
    /// Set true (one-way to the view-model) when this compositor can't import shared GPU textures,
    /// so the view-model can show its empty ghost instead of a silent black viewport.
    /// </summary>
    public static readonly StyledProperty<bool> InteropUnavailableProperty =
        AvaloniaProperty.Register<CompositionInteropViewport, bool>(nameof(InteropUnavailable));

    private Compositor? _compositor;
    private CompositionDrawingSurface? _drawingSurface;
    private CompositionSurfaceVisual? _visual;
    private ICompositionGpuInterop? _interop;
    private ICompositionImportedGpuImage? _imported;
    private Action? _update;
    private Task? _lastPresent;
    private bool _initialized;
    private bool _running;

    public CompositionInteropViewport()
    {
        ClipToBounds = true;
    }

    public ViewSurface? Surface
    {
        get => GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    public bool InteropUnavailable
    {
        get => GetValue(InteropUnavailableProperty);
        set => SetValue(InteropUnavailableProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
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

        var selfVisual = ElementComposition.GetElementVisual(this);
        if (selfVisual is null)
        {
            InteropUnavailable = true;
            return;
        }

        _compositor = selfVisual.Compositor;
        _drawingSurface = _compositor.CreateDrawingSurface();
        _visual = _compositor.CreateSurfaceVisual();
        _visual.Surface = _drawingSurface;
        ElementComposition.SetElementChildVisual(this, _visual);
        UpdateVisualLayout();

        _interop = await _compositor.TryGetCompositionGpuInterop();
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
        await ImportCurrentSurfaceAsync().ContinueOnAnyContext();
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
        // Drop the previous import only once any in-flight present that referenced it has finished.
        var old = _imported;
        _imported = null;
        if (old is not null)
        {
            if (_lastPresent is not null)
            {
                try
                {
                    await _lastPresent.ContinueOnAnyContext();
                }
                catch
                {
                    // A failed present must not block teardown of the old image.
                }
            }

            await old.DisposeAsync().ConfigureAwait(false);
        }

        var surface = Surface;
        if (_interop is null || surface is null || surface.Handle == 0)
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

    /// <summary>Letterboxes the shared texture into the control's bounds (Stretch=Uniform).</summary>
    private void UpdateVisualLayout()
    {
        if (_visual is null)
            return;

        var bounds = Bounds;
        var surface = Surface;
        if (surface is null || surface.Width <= 0 || surface.Height <= 0
            || bounds.Width <= 0 || bounds.Height <= 0)
        {
            _visual.Size = new Vector2((float)bounds.Width, (float)bounds.Height);
            _visual.Offset = new Vector3(0F, 0F, 0F);
            return;
        }

        var scale = Math.Min(bounds.Width / surface.Width, bounds.Height / surface.Height);
        var width = surface.Width * scale;
        var height = surface.Height * scale;
        _visual.Size = new Vector2((float)width, (float)height);
        _visual.Offset = new Vector3(
            (float)((bounds.Width - width) / 2),
            (float)((bounds.Height - height) / 2),
            0F);
    }

    private void TearDown()
    {
        _running = false;
        _initialized = false;

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
                await lastPresent.ContinueOnAnyContext();
            }
            catch
            {
                // Ignore a failed present; we still need to release the image.
            }
        }

        await imported.DisposeAsync().ConfigureAwait(false);
    }
}
