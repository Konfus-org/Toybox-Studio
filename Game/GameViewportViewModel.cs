using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;
using Toybox.Studio.Viewport;

namespace Toybox.Studio.Game;

/// <summary>
/// The game viewport: shows exactly what the game camera sees (the engine's mirrored game camera) and
/// forwards raw input to the running game. It composes the reusable <see cref="ViewportSurfaceViewModel"/>
/// for the frame surface and adds only the game's own input policy — the relative-mouse (mouselook) cursor
/// and the Esc-stops-play gesture. It owns no selection, billboards, marquee, or toolbar.
/// </summary>
public sealed partial class GameViewportViewModel : ObservableObject, IDisposable, IViewportInputSink
{
    private readonly Session _session;
    private readonly EngineWatcher _watcher;
    private readonly ViewportSurfaceViewModel _surface;
    private readonly Action<ConnectionState> _onStateChanged;
    private readonly Action<EngineState> _onEngineStateChanged;
    private readonly Action<string>? _onMouseLockChanged;

    public GameViewportViewModel(
        Session session, Func<ViewKind, ViewportStream> streamFactory, EngineWatcher watcher, Logger logger)
    {
        _session = session;
        _watcher = watcher;

        _surface = new ViewportSurfaceViewModel(session, watcher, logger, ViewKind.Game);
        _surface.Prepare(streamFactory(ViewKind.Game));

        // The game view offers the same launch button as the editor viewport while the engine is fully off
        // (not compiling, loading, or running); the watcher raises on the UI thread.
        IsEngineOff = watcher.State == EngineState.Off;
        _onEngineStateChanged = state =>
            Dispatch.To(DispatchContext.UI, () => IsEngineOff = state == EngineState.Off);
        watcher.StateChanged += _onEngineStateChanged;

        // The game mirrors its own mouse-lock mode so the panel can capture/release the cursor to match.
        _onStateChanged = state => Dispatch.To(DispatchContext.UI, () =>
        {
            if (state != ConnectionState.Connected)
                RelativeMouse = false;
        });
        session.StateChanged += _onStateChanged;

        if (_surface.Stream is { } stream)
        {
            _onMouseLockChanged = mode =>
                Dispatch.To(DispatchContext.UI, () => RelativeMouse = mode == "relative");
            stream.MouseLockModeChanged += _onMouseLockChanged;
        }
    }

    /// <summary>The reusable frame surface (engine game-camera mirror + ghost state).</summary>
    public ViewportSurfaceViewModel Surface => _surface;

    /// <summary>
    /// Whether the engine is fully off (disconnected and idle — not compiling, loading, or running). Drives
    /// the game view's launch button, which starts the engine from an empty frame just like the viewport's.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEngineOff { get; private set; }

    /// <summary>Compiles and launches the engine for the current project, from the empty-frame CTA.</summary>
    [RelayCommand]
    private Task LaunchEngine() => _session.StartAsync();

    /// <summary>
    /// Whether the playing game has requested relative-mouse (mouselook) mode. The panel hides and
    /// re-centres the cursor while true.
    /// </summary>
    [ObservableProperty]
    public partial bool RelativeMouse { get; private set; }

    /// <inheritdoc/>
    public void ForwardInput(ViewportInputPayload payload) => _surface.ForwardInput(payload);

    /// <inheritdoc/>
    public bool WantsPointerLock => RelativeMouse;

    /// <inheritdoc/>
    public bool AllowsMarquee => false;

    /// <inheritdoc/>
    public bool AllowsContextMenu => false;

    /// <summary>The game owns its input; a tap is just forwarded, not a pick.</summary>
    public void Tap(double x, double y, double width, double height, bool additive)
    {
    }

    /// <summary>The game view has no entity context menu (<see cref="AllowsContextMenu"/> is false), so this
    /// is never called; it picks nothing.</summary>
    public Task<ulong?> PickAndSelectForMenuAsync(double x, double y, double controlWidth, double controlHeight) =>
        Task.FromResult<ulong?>(null);

    /// <inheritdoc/>
    public void UpdateMarquee(double x, double y, double width, double height)
    {
    }

    /// <inheritdoc/>
    public void EndMarquee(
        double x, double y, double width, double height,
        double controlWidth, double controlHeight, bool additive)
    {
    }

    /// <inheritdoc/>
    public void CancelMarquee()
    {
    }

    /// <summary>Plain Esc stops play (the engine and viewports keep running).</summary>
    public bool HandleEscape()
    {
        _session.StopPlayAsync().FireAndForget();
        return true;
    }

    public void Dispose()
    {
        if (_onMouseLockChanged is not null && _surface.Stream is { } stream)
            stream.MouseLockModeChanged -= _onMouseLockChanged;
        _session.StateChanged -= _onStateChanged;
        _watcher.StateChanged -= _onEngineStateChanged;
        _surface.Dispose();
    }
}
