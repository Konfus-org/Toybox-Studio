using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// The reusable injection primitive: shows one engine view (an editor camera, the game camera, or an
/// asset-preview orbit) by owning its <see cref="ViewportStream"/> and surfacing the streamed shared GPU
/// texture, the loading/empty ghost state, and raw input forwarding. It is deliberately kind-agnostic —
/// it knows nothing about billboards, marquee selection, the transport, or preview options. A specific
/// viewport (editor / game / asset preview) embeds one of these and layers its own concerns on top, so
/// anything that needs to inject a 3D view just hands in a stream and binds a <see cref="ViewportSurfaceView"/>.
/// </summary>
/// <remarks>
/// Lifecycle: the host constructs the <see cref="ViewportStream"/> (which starts the engine view) and hands
/// it to <see cref="Prepare"/>; the shared texture then presents itself through the zero-copy compositor
/// binding — there is no per-frame CPU present. Disposing stops the engine view. The stream and watcher
/// outlive nothing here, so subscriptions are dropped on dispose.
/// </remarks>
public sealed partial class ViewportSurfaceViewModel : ObservableObject, IDisposable
{
    private readonly Session _session;
    private readonly EngineWatcher _watcher;
    private readonly Logger _logger;
    private readonly ViewKind _kind;
    private readonly Action<ConnectionState> _onStateChanged;
    private readonly Action<EngineState> _onWatcherStateChanged;
    private ViewportStream? _stream;

    public ViewportSurfaceViewModel(
        Session session, EngineWatcher watcher, Logger logger, ViewKind kind,
        string emptyMessage = "No world loaded.")
    {
        _session = session;
        _watcher = watcher;
        _logger = logger;
        _kind = kind;
        EmptyMessage = emptyMessage;

        _onStateChanged = state => Dispatch.To(DispatchContext.UI, () =>
        {
            if (state != ConnectionState.Connected)
                ClearSurface();
        });
        // The watcher already raises on the UI thread; re-derive the ghost state whenever it changes.
        _onWatcherStateChanged = _ => Dispatch.To(DispatchContext.UI, OnLoadingChanged);
        session.StateChanged += _onStateChanged;
        watcher.StateChanged += _onWatcherStateChanged;
    }

    /// <summary>
    /// Binds this surface to a started engine view. The host owns creating the stream (by kind, or by
    /// kind + asset for a preview); the surface owns its lifetime from here — the shared texture follows as
    /// a <c>view.surface</c> notification and presents through the interop control. Replacing a previous
    /// stream stops it first.
    /// </summary>
    public void Prepare(ViewportStream stream)
    {
        if (ReferenceEquals(_stream, stream))
            return;

        DropStream();
        _stream = stream;
        _stream.SurfaceArrived += OnSurfaceArrived;
        _stream.SurfaceLost += OnSurfaceLost;
    }

    /// <summary>
    /// The bound engine-view link, for the host's view queries (pick / project / occlusion / frame) and
    /// mouse-lock notifications. Null until <see cref="Prepare"/> has run.
    /// </summary>
    public ViewportStream? Stream => _stream;

    /// <summary>
    /// The engine view's shared GPU texture, bound by the view into the interop control. Null while there
    /// is nothing to show (disconnected, not yet ready, or GPU sharing unavailable).
    /// </summary>
    [ObservableProperty]
    public partial ViewSurface? CurrentSurface { get; private set; }

    /// <summary>Whether a real frame is on screen — gates the host's overlays (billboards, toolbar).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyGhost))]
    public partial bool HasFrames { get; private set; }

    /// <summary>
    /// Set by the interop control (bound one-way to source) when this editor's compositor can't import
    /// shared GPU textures. Forces the empty ghost so the viewport is never silently black.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyGhost))]
    public partial bool InteropUnavailable { get; set; }

    /// <summary>The empty-state ghost message ("No world loaded.", or a preview's own text).</summary>
    public string EmptyMessage { get; }

    /// <summary>The phase text shown by the loading ghost (e.g. "Compiling project…").</summary>
    public string LoadingMessage => _watcher.StatusMessage;

    /// <summary>
    /// The loading ghost shows while compiling or loading. A play transition ("Loading into game…") is the
    /// game's load, so only the game surface shows it; an asset-preview surface (its own isolated world)
    /// never participates in the active world's load.
    /// </summary>
    public bool ShowLoadingGhost =>
        _kind != ViewKind.AssetPreview
        && _watcher.State is EngineState.Compiling or EngineState.Loading
        && (!_watcher.IsGameLoad || _kind == ViewKind.Game);

    /// <summary>The empty-state ghost shows when there's nothing to draw (or we can't draw it) and the
    /// loading ghost isn't up (it owns the busy state), so the two never overlap.</summary>
    public bool ShowEmptyGhost => (!HasFrames || InteropUnavailable) && !ShowLoadingGhost;

    /// <summary>
    /// Forwards a captured input snapshot to the engine view, including the cursor mapped to normalized
    /// image coordinates (for the gizmo). A no-op until a stream is bound.
    /// </summary>
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

    public void Dispose()
    {
        _session.StateChanged -= _onStateChanged;
        _watcher.StateChanged -= _onWatcherStateChanged;
        DropStream();
    }

    private void DropStream()
    {
        if (_stream is null)
            return;

        _stream.SurfaceArrived -= OnSurfaceArrived;
        _stream.SurfaceLost -= OnSurfaceLost;
        _stream.Dispose();
        _stream = null;
        ClearSurface();
    }

    private void OnLoadingChanged()
    {
        OnPropertyChanged(nameof(LoadingMessage));
        OnPropertyChanged(nameof(ShowLoadingGhost));
        OnPropertyChanged(nameof(ShowEmptyGhost));
    }

    partial void OnInteropUnavailableChanged(bool value)
    {
        if (value)
            _logger.Error(
                "This editor's compositor cannot import shared GPU textures (composition GPU interop "
                + "unavailable), so engine viewports cannot be displayed. Ensure the editor and engine "
                + "run on the same GPU adapter.");
    }

    private void OnSurfaceArrived(ViewSurface surface) =>
        Dispatch.To(DispatchContext.UI, () => ApplySurface(surface));

    private void OnSurfaceLost() =>
        Dispatch.To(DispatchContext.UI, ClearSurface);

    private void ApplySurface(ViewSurface surface)
    {
        if (surface.Handle == 0)
        {
            // The engine logs the underlying reason (and streams it to our console); note it here too so
            // it's clear the empty viewport is a capability gap, not a missing world.
            _logger.Warning(
                "GPU texture sharing is unavailable for this view; the viewport will stay empty. "
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
