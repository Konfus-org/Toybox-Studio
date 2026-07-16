using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Hosting;
using Toybox.Studio.Input;
using Toybox.Studio.Logging;
using Toybox.Studio.Toolbar;
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
    IToolbarHost,
    IEventHandler<ConnectionChanged>,
    IEventHandler<EngineStateChanged>,
    IEventHandler<ViewSurfaceCreated>
{
    private readonly Logger _logger;
    private readonly ViewKind _kind;
    private readonly IViewportPickHandler? _pick;
    private readonly ViewportTapTracker _taps = new();
    private ViewportStream? _stream;

    // The engine's derived state, copied here on the (UI-thread) EngineStateChanged events so the
    // ghost properties re-derive without holding the engine itself.
    private EngineState _engineState = EngineState.Off;

    public ViewportViewModel(
        EventDispatcher events,
        Logger logger,
        Engine engine,
        ViewKind kind,
        string emptyMessage = "No world loaded.",
        string preparingMessage = "Loading…",
        IReadOnlyList<ToolbarViewModel>? toolbars = null,
        IViewportPickHandler? pick = null,
        object? overlay = null)
        : base(events)
    {
        _logger = logger;
        _kind = kind;
        _pick = pick;
        EmptyMessage = emptyMessage;
        PreparingMessage = preparingMessage;
        Toolbars = toolbars ?? [];
        Overlay = overlay;

        // Seed the authoritative engine state so a viewport opened while the engine is off shows the launch
        // prompt at once (rather than waiting on a state change that a steady-off engine never dispatches),
        // and one opened over a running engine never flashes it. The engine isn't held — later changes
        // arrive as EngineStateChanged.
        _engineState = engine.State;
        _engineStatus = engine.State.StatusMessage;
        IsEngineOff = engine.State.Phase == EnginePhase.Off;
    }

    /// <summary>The overlay toolbars (the transform tools, the render layers), empty for a viewport
    /// kind without them (game, asset preview). The host composes them; this panel owns their
    /// lifetimes and their layout-persisted placements.</summary>
    public IReadOnlyList<ToolbarViewModel> Toolbars { get; }

    /// <summary>An optional content overlay the view hosts over the render surface, sized to the surface's
    /// image rect (the node overlay for an editor viewport; null otherwise). Kind-agnostic — the view
    /// resolves its view by convention. This panel owns its lifetime.</summary>
    public object? Overlay { get; }

    /// <summary>The toolbars overlay only a live image (and only on a viewport that has them).</summary>
    public bool ShowToolbars => HasFrames && Toolbars.Count > 0;

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
    /// Drops the current stream and clears the surface without binding a new one — the host is hiding this
    /// viewport (e.g. an asset-browser hover preview whose pointer left). Stops the engine view (releasing
    /// its preview world); a later <see cref="Prepare"/> rebinds a fresh one.
    /// </summary>
    public void Detach() => DropStream();

    /// <summary>
    /// The engine view's shared GPU texture, imported and displayed by the view. Null while there is
    /// nothing to show (disconnected, not yet ready, or GPU sharing unavailable).
    /// </summary>
    [ObservableProperty]
    public partial ViewSurfaceCreated? CurrentSurface { get; private set; }

    /// <summary>Whether a real frame is on screen — gates the host's overlays.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyGhost))]
    [NotifyPropertyChangedFor(nameof(ShowToolbars))]
    [NotifyPropertyChangedFor(nameof(ShowLaunchPrompt))]
    public partial bool HasFrames { get; private set; }

    /// <summary>
    /// Set by the view (bound one-way to source) when this editor's compositor can't import shared GPU
    /// textures. Forces the empty ghost so the viewport is never silently black.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyGhost))]
    [NotifyPropertyChangedFor(nameof(ShowLoadingGhost))]
    [NotifyPropertyChangedFor(nameof(ShowLaunchPrompt))]
    public partial bool InteropUnavailable { get; set; }

    /// <summary>
    /// Set true by the host while it prepares this viewport's content — the Asset Viewer building its
    /// isolated preview world and streaming the asset in, work the engine's world-load state doesn't cover.
    /// Drives the loading ghost so a slow open shows a spinner instead of a blank panel; it clears itself
    /// the moment the first real frame lands (see <see cref="OnHasFramesChanged"/>).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingGhost))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyGhost))]
    [NotifyPropertyChangedFor(nameof(ShowLaunchPrompt))]
    [NotifyPropertyChangedFor(nameof(LoadingMessage))]
    public partial bool IsPreparing { get; set; }

    /// <summary>The empty-state ghost message ("No world loaded.", or a preview's own text).</summary>
    public string EmptyMessage { get; }

    /// <summary>The loading-ghost message shown while the host is <see cref="IsPreparing"/> (e.g. "Loading
    /// asset…"), as opposed to the engine's own compile/load status.</summary>
    public string PreparingMessage { get; }

    // The engine's latest compile/load status text, copied on EngineStateChanged; surfaced through
    // LoadingMessage when the ghost is up for an engine load rather than a host prepare.
    private string _engineStatus = string.Empty;

    /// <summary>The phase text shown by the loading ghost: the host's preparing text while preparing, else
    /// the engine's status (e.g. "Compiling project…").</summary>
    public string LoadingMessage => IsPreparing ? PreparingMessage : _engineStatus;

    /// <summary>
    /// The loading ghost shows while the host is preparing this viewport's content, or while the engine is
    /// compiling/loading. A play transition ("Loading into game…") is the game's load, so only the game
    /// viewport shows that; an asset-preview viewport (its own isolated world) never participates in the
    /// active world's load, but it does drive its own <see cref="IsPreparing"/> state. It never shows over
    /// an unusable compositor (<see cref="InteropUnavailable"/>) — the empty ghost owns that so the
    /// spinner can't run forever against a viewport that can never present.
    /// </summary>
    public bool ShowLoadingGhost =>
        !InteropUnavailable
        && (IsPreparing
            || (_kind != ViewKind.AssetPreview
                && _engineState.IsLoading
                && (!_engineState.IsGameLoading || _kind == ViewKind.Game)));

    /// <summary>The empty-state ghost shows when there's nothing to draw (or we can't draw it) and the
    /// loading ghost isn't up (it owns the busy state), so the two never overlap.</summary>
    public bool ShowEmptyGhost => (!HasFrames || InteropUnavailable) && !ShowLoadingGhost;

    /// <summary>
    /// True when the engine is fully off (not compiling, launching, loading, or connected) — it crashed and
    /// auto-restart gave up, it was stopped, or it was never launched. Seeded from the engine's authoritative
    /// state at construction and kept current on <see cref="EngineStateChanged"/>; gates the launch prompt.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLaunchPrompt))]
    public partial bool IsEngineOff { get; private set; }

    /// <summary>
    /// The empty viewport's "Launch Engine" call-to-action: shown whenever the engine is off and the empty
    /// ghost is up, so every viewport (editor, game, asset preview) offers to (re)start the engine rather
    /// than sitting as a dead black panel. It rides with the empty ghost and so never overlaps the loading
    /// spinner.
    /// </summary>
    public bool ShowLaunchPrompt => IsEngineOff && ShowEmptyGhost;

    /// <summary>
    /// Streams a captured input snapshot (already pointer-mapped by the view) to the engine view (a
    /// no-op until a stream is bound). A snapshot completing a tap also picks: the click-select
    /// gesture, on a viewport composed with an <see cref="IViewportPickHandler"/> (a plain viewport —
    /// game, asset preview — hands in none, so the tap path stays inert).
    /// </summary>
    public void ForwardInput(InputSnapshot input)
    {
        if (_pick is not null && _taps.Track(input) is { } tap)
            PickAsync(tap).FireAndForget();

        _stream?.SendInput(input);
    }

    /// <summary>
    /// Reports the viewport's on-screen size in device pixels (the view supplies it from its surface bounds
    /// × render scaling). Forwarded to the stream so an editor view renders at its pane's resolution rather
    /// than the full graphics resolution; a no-op until a stream is bound.
    /// </summary>
    public void UpdateRenderSize(int pixelWidth, int pixelHeight) =>
        _stream?.SetRenderSize(pixelWidth, pixelHeight);

    // Asks the engine what the tap hit (a generic per-view RPC), then hands the entity id to the pick
    // handler, which applies the host's semantics (the world viewport lands it on WorldSelection). A
    // gizmo-handle tap is deliberate no-op territory: forwarding it would disturb the gizmo the click
    // is driving.
    private async Task PickAsync(ViewportTap tap)
    {
        if (_stream is null)
            return;

        var result = await _stream.PickAsync(tap.U, tap.V).ContinueOnAnyContext();
        if (!result || result.Value is not { } pick)
            return; // A dropped connection mid-click; nothing to select against.

        if (pick.Gizmo)
            return;

        Dispatch.To(DispatchContext.UI, () => _pick?.OnPicked(pick.Id, tap));
    }

    /// <summary>The workspace hands in this panel's layout-persisted toolbar placements (see
    /// <see cref="IToolbarHost"/>); idempotent across a restore's repeated attach passes.</summary>
    public void BindToolbars(ToolbarDockStates states)
    {
        foreach (var toolbar in Toolbars)
            toolbar.BindDockState(states.For(toolbar.Key, toolbar.DefaultEdge));
    }

    public override void Dispose()
    {
        base.Dispose();
        DropStream();
        foreach (var toolbar in Toolbars)
            toolbar.Dispose();
        (Overlay as IDisposable)?.Dispose();
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
        _engineStatus = evt.State.StatusMessage;
        IsEngineOff = evt.State.Phase == EnginePhase.Off; // Notifies ShowLaunchPrompt.
        OnPropertyChanged(nameof(LoadingMessage));
        OnPropertyChanged(nameof(ShowLoadingGhost));
        OnPropertyChanged(nameof(ShowEmptyGhost));
    }

    /// <summary>Builds the active project and launches its engine, from the empty viewport's call-to-action.
    /// The viewport doesn't own the engine host: it announces the request and the Projects-layer coordinator
    /// carries it out (the same path startup and project-switch take).</summary>
    [RelayCommand]
    private void LaunchEngine() => Events.Dispatch(new EngineLaunchRequested());

    // A real frame on screen means the host's preparation is over — drop the loading ghost automatically so
    // the host never has to race the surface. (A host prepare that fails before any frame clears it itself.)
    partial void OnHasFramesChanged(bool value)
    {
        if (value)
            IsPreparing = false;
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
