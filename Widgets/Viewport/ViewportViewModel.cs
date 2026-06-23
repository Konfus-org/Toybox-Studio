using Toybox.Studio.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using System.Threading.Tasks;
using Toybox.Studio.Services.World;
using Toybox.Studio.Shell.Panels;
using Toybox.Studio.Shell.Workspace;
using Toybox.Studio.Widgets.Toolbar;

namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// The reusable "frame surface": shows one engine view (an editor camera or the game camera) by
/// handing its shared GPU texture to a <see cref="CompositionInteropViewport"/>, which the
/// compositor samples directly — no CPU readback, no per-frame upload. Each instance owns its own
/// <see cref="ViewportStream"/>, so several can stream side by side; disposing it stops the engine
/// view. Used directly by every editor viewport instance and embedded by the game view.
/// </summary>
public sealed partial class ViewportViewModel : DataPanel, IDisposable, IViewportInputSink, IToolbarHost
{
    private readonly ViewportStream _stream;
    private readonly Action<ConnectionState> _onStateChanged;
    private readonly Action<EngineState> _onWatcherStateChanged;
    private readonly Action<string>? _onMouseLockChanged;
    private readonly Action<bool> _onWorldDirtyChanged;
    private readonly Session _session;
    private readonly EngineWatcher _watcher;
    private readonly WorldManager _world;
    private readonly WorldSelection _selection;
    private readonly GizmoTool _gizmoTool;
    private readonly ToolCommandRunner _toolCommandRunner;
    private readonly ToolbarState _toolbarState;
    private readonly Logger _logger;

    private readonly ViewKind _kind;

    public ViewportViewModel(
        Session session, Func<ViewKind, ViewportStream> streamFactory, Logger logger, EngineWatcher watcher,
        WorldManager world, WorldSelection selection, GizmoTool gizmoTool, ToolCommandRunner toolCommandRunner,
        ToolbarState toolbarState, ViewKind kind = ViewKind.Editor)
    {
        _session = session;
        _watcher = watcher;
        _world = world;
        _selection = selection;
        _gizmoTool = gizmoTool;
        _toolCommandRunner = toolCommandRunner;
        _toolbarState = toolbarState;
        _logger = logger;
        _kind = kind;
        _gizmoTool.Changed += OnGizmoToolChanged;

        // Editor viewports carry a movable, data-driven toolbar. Seed a default now so a viewport restored
        // from a pre-toolbar layout still shows one; BindToolbar swaps in the persisted layout when the dock
        // tool that owns it materializes. The game panel never shows a toolbar.
        if (!IsGame)
            Toolbar = new ToolbarViewModel(ToolbarLayout.Default(), _toolCommandRunner, _toolbarState, _watcher);

        _stream = streamFactory(kind);
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
            _stream.MouseLockModeChanged += _onMouseLockChanged;
        }
    }

    /// <summary>The dock-tab base title; the '*' is appended by <see cref="DataPanel"/> while the world is dirty.</summary>
    public override string BaseTitle => "Viewport";

    /// <summary>The viewport is a LIVE panel: world edits commit immediately, so it buffers nothing, has no
    /// Save/Cancel footer, and never prompts on tab close. Saving persists the live world.</summary>
    public override bool IsLive => true;

    /// <summary>Whether this surface shows the game (vs an editor viewport).</summary>
    public bool IsGame => _kind == ViewKind.Game;

    /// <summary>
    /// Whether the playing game has requested relative-mouse (mouselook) mode. The game panel hides and
    /// re-centres the cursor while true; always false for editor viewports.
    /// </summary>
    [ObservableProperty]
    public partial bool RelativeMouse { get; private set; }

    /// <summary>Whether a left-drag should box-select — only with the select tool (and never the game).</summary>
    public bool MarqueeEnabled => !IsGame && _gizmoTool.Mode == GizmoMode.None;

    /// <summary>
    /// The viewport's movable, data-driven toolbar (the transform tools by default), or null for the game
    /// panel. The view-model is bound to the persisted <see cref="ToolbarLayout"/> via <see cref="BindToolbar"/>.
    /// </summary>
    [ObservableProperty]
    public partial ToolbarViewModel? Toolbar { get; private set; }

    /// <summary>The rubber-band marquee rectangle (control space); the view draws it while visible.</summary>
    [ObservableProperty]
    public partial bool MarqueeVisible { get; private set; }

    [ObservableProperty]
    public partial double MarqueeX { get; private set; }

    [ObservableProperty]
    public partial double MarqueeY { get; private set; }

    [ObservableProperty]
    public partial double MarqueeWidth { get; private set; }

    [ObservableProperty]
    public partial double MarqueeHeight { get; private set; }

    /// <summary>
    /// The engine view's shared GPU texture, bound by the view into the interop control. Null while
    /// there is nothing to show (disconnected, not yet ready, or GPU sharing unavailable).
    /// </summary>
    [ObservableProperty]
    public partial ViewSurface? CurrentSurface { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyGhost))]
    [NotifyPropertyChangedFor(nameof(ShowToolbar))]
    public partial bool HasFrames { get; private set; }

    /// <summary>The viewport toolbar shows over an editor viewport once it has frames (never the game).</summary>
    public bool ShowToolbar => !IsGame && HasFrames;

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
        _gizmoTool.Changed -= OnGizmoToolChanged;
        if (_onMouseLockChanged is not null)
            _stream.MouseLockModeChanged -= _onMouseLockChanged;
        Toolbar?.Dispose();
        _stream.Dispose();
    }

    /// <summary>Forwards captured viewport input to this surface's engine view, including the cursor mapped
    /// to normalized image coordinates (for the gizmo).</summary>
    public void ForwardInput(ViewportInputPayload payload)
    {
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

    /// <summary>
    /// Binds the persisted toolbar layout the hosting dock tool owns (so toolbar edits persist with the
    /// layout). Idempotent: re-binding the same layout instance is a no-op.
    /// </summary>
    public void BindToolbar(ToolbarLayout layout)
    {
        if (IsGame || ReferenceEquals(Toolbar?.Layout, layout))
            return;

        Toolbar?.Dispose();
        Toolbar = new ToolbarViewModel(layout, _toolCommandRunner, _toolbarState, _watcher);
    }

    /// <summary>Stops play mode (the game view's Esc); the engine and viewports keep running.</summary>
    public void StopGame() => _session.StopPlayAsync().FireAndForget();

    /// <summary>
    /// Picks the entity at a viewport click and updates the shared selection. The point is in control space;
    /// it's mapped to the rendered image, then the engine resolves the hit. A miss (the engine resolves no
    /// entity) clears the selection unless this is an additive (Shift) click. No-op for the game.
    /// </summary>
    public void Pick(double pointerX, double pointerY, double controlWidth, double controlHeight, bool additive)
    {
        if (IsGame)
            return;

        var surface = CurrentSurface;
        if (surface is null
            || !ViewportMapping.TryNormalize(
                pointerX, pointerY, controlWidth, controlHeight, surface.Width, surface.Height,
                out var u, out var v))
        {
            // Click with no frame to pick against: clear unless adding to the selection.
            if (!additive)
                _selection.Clear();
            return;
        }

        PickAtAsync(u, v, additive).FireAndForget();
    }

    /// <summary>Shows/updates the live marquee rectangle (control space) during a left-drag.</summary>
    public void UpdateMarquee(double x, double y, double width, double height)
    {
        MarqueeX = x;
        MarqueeY = y;
        MarqueeWidth = width;
        MarqueeHeight = height;
        MarqueeVisible = true;
    }

    /// <summary>Hides the marquee without selecting.</summary>
    public void CancelMarquee() => MarqueeVisible = false;

    /// <summary>Commits a marquee box-select (drag release) and hides the marquee.</summary>
    public void EndMarquee(
        double x, double y, double width, double height,
        double controlWidth, double controlHeight, bool additive)
    {
        MarqueeVisible = false;
        if (IsGame)
            return;

        var surface = CurrentSurface;
        if (surface is null)
            return;

        var (u0, v0) = ViewportMapping.NormalizeClamped(
            x, y, controlWidth, controlHeight, surface.Width, surface.Height);
        var (u1, v1) = ViewportMapping.NormalizeClamped(
            x + width, y + height, controlWidth, controlHeight, surface.Width, surface.Height);
        PickRectAsync(u0, v0, u1, v1, additive).FireAndForget();
    }

    private void OnGizmoToolChanged() =>
        Dispatch.To(DispatchContext.UI, () => OnPropertyChanged(nameof(MarqueeEnabled)));

    private async Task PickAtAsync(double u, double v, bool additive)
    {
        var result = await _stream.PickAsync(u, v).ContinueOnAnyContext();
        if (!result.Success)
            return; // Engine gone or no rendering service; leave the selection untouched.

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

    private async Task PickRectAsync(double u0, double v0, double u1, double v1, bool additive)
    {
        var result = await _stream.PickRectAsync(u0, v0, u1, v1).ContinueOnAnyContext();
        if (!result.Success)
            return;

        var ids = result.Value!;
        Dispatch.To(DispatchContext.UI, () =>
        {
            if (additive)
                _selection.SetMany(_selection.SelectedIds.Concat(ids).Distinct().ToList());
            else
                _selection.SetMany(ids);
        });
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
