using Toybox.Studio.Utils;
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using System.Threading.Tasks;
using Toybox.Studio.Services.World;
using Toybox.Studio.Shell.Panels;

namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// The reusable "frame surface": shows one engine view (an editor camera or the game camera) by
/// handing its shared GPU texture to a <see cref="CompositionInteropViewport"/>, which the
/// compositor samples directly — no CPU readback, no per-frame upload. Each instance owns its own
/// <see cref="ViewportStream"/>, so several can stream side by side; disposing it stops the engine
/// view. Used directly by every editor viewport instance and embedded by the game view.
/// </summary>
public sealed partial class ViewportViewModel : DataPanel, IDisposable, IViewportInputSink
{
    private readonly ViewportStream _stream;
    private readonly EngineRpc _engine;
    private readonly Action<ConnectionState> _onStateChanged;
    private readonly Action<EngineState> _onWatcherStateChanged;
    private readonly Action<string>? _onMouseLockChanged;
    private readonly Action<bool> _onWorldDirtyChanged;
    private readonly Session _session;
    private readonly EngineWatcher _watcher;
    private readonly WorldManager _world;
    private readonly Logger _logger;

    private readonly ViewKind _kind;

    public ViewportViewModel(
        Session session, EngineRpc engine, Logger logger, EngineWatcher watcher, WorldManager world,
        ViewKind kind = ViewKind.Editor)
    {
        _session = session;
        _engine = engine;
        _watcher = watcher;
        _world = world;
        _logger = logger;
        _kind = kind;
        _stream = new ViewportStream(session, engine, kind);
        _stream.SurfaceArrived += OnSurfaceArrived;
        _stream.SurfaceLost += OnSurfaceLost;

        // The viewport shows the live world, so its tab carries the world's unsaved-changes '*'. No Save/Cancel
        // footer: it isn't a document — saving the world is an explicit editor action elsewhere.
        IsDirty = world.IsDirty;
        _onWorldDirtyChanged = dirty => Dispatch.To(DispatchContext.UI, () => IsDirty = dirty);
        world.DirtyChanged += _onWorldDirtyChanged;

        _onStateChanged = state => Dispatch.To(DispatchContext.UI, () =>
        {
            if (state != ConnectionState.Connected)
            {
                ClearSurface();
                RelativeMouse = false;
            }
        });
        // The watcher already raises on the UI thread; re-derive the ghost state whenever it changes.
        _onWatcherStateChanged = _ => Dispatch.To(DispatchContext.UI, OnLoadingChanged);
        session.StateChanged += _onStateChanged;
        watcher.StateChanged += _onWatcherStateChanged;

        // Only the game panel mirrors the game's mouse-lock mode; editor viewports keep a normal cursor.
        if (IsGame)
        {
            _onMouseLockChanged = mode =>
                Dispatch.To(DispatchContext.UI, () => RelativeMouse = mode == "relative");
            engine.MouseLockModeChanged += _onMouseLockChanged;
        }
    }

    /// <summary>The dock-tab base title; the '*' is appended by <see cref="DataPanel"/> while the world is dirty.</summary>
    public override string BaseTitle => "Viewport";

    /// <summary>The viewport is a LIVE panel: world edits commit immediately, so it buffers nothing, has no
    /// Save/Cancel footer, and never prompts on tab close. Saving persists the live world.</summary>
    public override bool IsLive => true;

    /// <summary>File ▸ Save on a focused viewport persists the live world.</summary>
    public override Task SaveAsync() => _world.SaveAsync();

    /// <summary>Stops this surface's engine view and unhooks from the session.</summary>
    public void Dispose()
    {
        _stream.SurfaceArrived -= OnSurfaceArrived;
        _stream.SurfaceLost -= OnSurfaceLost;
        _session.StateChanged -= _onStateChanged;
        _watcher.StateChanged -= _onWatcherStateChanged;
        _world.DirtyChanged -= _onWorldDirtyChanged;
        if (_onMouseLockChanged is not null)
            _engine.MouseLockModeChanged -= _onMouseLockChanged;
        _stream.Dispose();
    }

    /// <summary>Whether this surface shows the game (vs an editor viewport).</summary>
    public bool IsGame => _kind == ViewKind.Game;

    /// <summary>
    /// Whether the playing game has requested relative-mouse (mouselook) mode. The game panel hides and
    /// re-centres the cursor while true; always false for editor viewports.
    /// </summary>
    [ObservableProperty]
    public partial bool RelativeMouse { get; private set; }

    /// <summary>Forwards captured viewport input to this surface's engine view.</summary>
    public void ForwardInput(ViewportInputPayload payload) =>
        _stream.SendInput(
            payload.Focused, payload.Buttons, payload.MoveKeys, payload.Keys,
            payload.MouseX, payload.MouseY, payload.Dx, payload.Dy, payload.Wheel);

    /// <summary>Stops play mode (the game view's Esc); the engine and viewports keep running.</summary>
    public void StopGame() => _session.StopPlayAsync().FireAndForget();

    /// <summary>
    /// The engine view's shared GPU texture, bound by the view into the interop control. Null while
    /// there is nothing to show (disconnected, not yet ready, or GPU sharing unavailable).
    /// </summary>
    [ObservableProperty]
    public partial ViewSurface? CurrentSurface { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyGhost))]
    public partial bool HasFrames { get; private set; }

    /// <summary>
    /// Set by the interop control (bound one-way to source) when this editor's compositor can't
    /// import shared GPU textures. Forces the empty ghost so the viewport is never silently black.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyGhost))]
    public partial bool InteropUnavailable { get; set; }

    /// <summary>The phase text shown by the loading ghost (e.g. "Compiling project…").</summary>
    public string LoadingMessage => _watcher.StatusMessage;

    /// <summary>
    /// The loading ghost shows while compiling or loading. A play transition ("Loading into game…")
    /// is the game's load, so only the game viewport shows it — editor viewports keep their live frame.
    /// </summary>
    public bool ShowLoadingGhost =>
        (_watcher.State is EngineState.Compiling or EngineState.Loading)
        && (!_watcher.IsGameLoad || IsGame);

    /// <summary>The empty-state ghost shows when there's nothing to draw (or we can't draw it) and
    /// the loading ghost isn't up (it owns the busy state), so the two never overlap.</summary>
    public bool ShowEmptyGhost => (!HasFrames || InteropUnavailable) && !ShowLoadingGhost;

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
            // The engine logs the underlying reason (and streams it to our console); note it here too
            // so it's clear the empty viewport is a capability gap, not a missing world.
            _logger.Warning(
                $"GPU texture sharing is unavailable for this view; the viewport will stay empty. "
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
