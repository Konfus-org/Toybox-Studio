using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Utils;
using Toybox.Studio.Widgets.Viewport;

namespace Toybox.Studio.Widgets.GameView;

/// <summary>
/// The game viewport: shows exactly what the game camera sees (the engine's mirrored game camera) and
/// forwards raw input to the running game. It composes the reusable <see cref="ViewportSurfaceViewModel"/>
/// for the frame surface and adds only the game's own input policy — the relative-mouse (mouselook) cursor
/// and the Esc-stops-play gesture. It owns no selection, billboards, marquee, or toolbar.
/// </summary>
public sealed partial class GameViewportViewModel : ObservableObject, IDisposable, IViewportInputSink
{
    private readonly Session _session;
    private readonly ViewportSurfaceViewModel _surface;
    private readonly Action<ConnectionState> _onStateChanged;
    private readonly Action<string>? _onMouseLockChanged;

    public GameViewportViewModel(
        Session session, Func<ViewKind, ViewportStream> streamFactory, EngineWatcher watcher, Logger logger)
    {
        _session = session;

        _surface = new ViewportSurfaceViewModel(session, watcher, logger, ViewKind.Game);
        _surface.Prepare(streamFactory(ViewKind.Game));

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
        _surface.Dispose();
    }
}
