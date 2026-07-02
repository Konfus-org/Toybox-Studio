using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Input;
using Toybox.Studio.AppHosting;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Viewport;

/// <summary>
/// Backs the <see cref="ViewportView"/>: owns the view's <see cref="ViewportStream"/> and surfaces the
/// streamed shared GPU texture, the loading/empty ghost state, and input forwarding. It is deliberately
/// kind-agnostic — a specific viewport (editor / game / asset preview) hands in a stream. It learns
/// everything by handling the engine layer's dispatched events: the globally announced
/// <see cref="ViewSurfaceCreated"/>s (filtered to its own stream's view), connection drops, and the engine's
/// derived <see cref="EngineState"/> for the ghosts.
/// </summary>
/// <remarks>
/// Lifecycle: the host constructs the <see cref="ViewportStream"/> (which starts the engine view) and hands
/// it to <see cref="Prepare"/>; the shared texture then presents itself through the zero-copy compositor
/// binding — there is no per-frame CPU present. Disposing stops the engine view.
/// </remarks>
public sealed partial class ViewportViewModel :
    ObservableEventSubscriber,
    IEventHandler<ConnectionChanged>,
    IEventHandler<EngineStateChanged>,
    IEventHandler<ViewSurfaceCreated>
{
    private readonly Logger _logger;
    private readonly ViewKind _kind;
    private ViewportStream? _stream;

    // The engine's derived state, copied here on the (UI-thread) EngineStateChanged events so the
    // ghost properties re-derive without holding the engine itself.
    private EngineState _engineState = EngineState.Off;

    public ViewportViewModel(
        EventDispatcher events, Logger logger, ViewKind kind, string emptyMessage = "No world loaded.")
        : base(events)
    {
        _logger = logger;
        _kind = kind;
        EmptyMessage = emptyMessage;
    }

    /// <summary>
    /// Binds this viewport to a started engine view. The host owns creating the stream (by kind, or by
    /// kind + asset for a preview); the viewport owns its lifetime from here — the shared texture follows
    /// as a dispatched <see cref="ViewSurfaceCreated"/>. Replacing a previous stream stops it first.
    /// </summary>
    public void Prepare(ViewportStream stream)
    {
        if (ReferenceEquals(_stream, stream))
            return;

        DropStream();
        _stream = stream;
    }

    /// <summary>
    /// The engine view's shared GPU texture, imported and displayed by the view. Null while there is
    /// nothing to show (disconnected, not yet ready, or GPU sharing unavailable).
    /// </summary>
    [ObservableProperty]
    public partial ViewSurfaceCreated? CurrentSurface { get; private set; }

    /// <summary>Whether a real frame is on screen — gates the host's overlays.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyGhost))]
    public partial bool HasFrames { get; private set; }

    /// <summary>
    /// Set by the view (bound one-way to source) when this editor's compositor can't import shared GPU
    /// textures. Forces the empty ghost so the viewport is never silently black.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyGhost))]
    public partial bool InteropUnavailable { get; set; }

    /// <summary>The empty-state ghost message ("No world loaded.", or a preview's own text).</summary>
    public string EmptyMessage { get; }

    /// <summary>The phase text shown by the loading ghost (e.g. "Compiling project…").</summary>
    [ObservableProperty]
    public partial string LoadingMessage { get; private set; } = string.Empty;

    /// <summary>
    /// The loading ghost shows while compiling or loading. A play transition ("Loading into game…") is the
    /// game's load, so only the game viewport shows it; an asset-preview viewport (its own isolated world)
    /// never participates in the active world's load.
    /// </summary>
    public bool ShowLoadingGhost =>
        _kind != ViewKind.AssetPreview
        && _engineState.IsLoading
        && (!_engineState.IsGameLoading || _kind == ViewKind.Game);

    /// <summary>The empty-state ghost shows when there's nothing to draw (or we can't draw it) and the
    /// loading ghost isn't up (it owns the busy state), so the two never overlap.</summary>
    public bool ShowEmptyGhost => (!HasFrames || InteropUnavailable) && !ShowLoadingGhost;

    /// <summary>
    /// Streams a captured input snapshot (already pointer-mapped by the view) to the engine view.
    /// A no-op until a stream is bound.
    /// </summary>
    public void ForwardInput(InputSnapshot input) => _stream?.SendInput(input);

    public override void Dispose()
    {
        base.Dispose();
        DropStream();
    }

    public void Handle(in ConnectionChanged evt)
    {
        if (evt.State != ConnectionState.Connected)
            Dispatch.To(DispatchContext.UI, ClearSurface);
    }

    // Dispatched on the UI thread by the engine.
    public void Handle(in EngineStateChanged evt)
    {
        _engineState = evt.State;
        LoadingMessage = evt.State.StatusMessage;
        OnPropertyChanged(nameof(ShowLoadingGhost));
        OnPropertyChanged(nameof(ShowEmptyGhost));
    }

    public void Handle(in ViewSurfaceCreated evt)
    {
        // Every view's surface is announced globally; take only our stream's.
        if (evt.Name != _stream?.ViewName)
            return;

        var surface = evt;
        Dispatch.To(DispatchContext.UI, () => ApplySurface(surface));
    }

    private void DropStream()
    {
        if (_stream is null)
            return;

        _stream.Dispose();
        _stream = null;
        ClearSurface();
    }

    partial void OnInteropUnavailableChanged(bool value)
    {
        if (value)
            _logger.Error(
                "This editor's compositor cannot import shared GPU textures (composition GPU interop "
                + "unavailable), so engine viewports cannot be displayed. Ensure the editor and engine "
                + "run on the same GPU adapter.");
    }

    private void ApplySurface(ViewSurfaceCreated surface)
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
