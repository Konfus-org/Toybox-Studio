using Toybox.Studio.Utils;
namespace Toybox.Studio.Services.EngineApi;

/// <summary>
/// The one place that watches the engine and answers "what is it doing?". It folds the scattered
/// session/engine signals (connection state, compile phase, play transition, and the first-frame
/// <c>view.presented</c> notification) into a single <see cref="EngineState"/> plus a human-readable
/// <see cref="StatusMessage"/>, and raises <see cref="StateChanged"/> / <see cref="LoadCompleted"/>
/// so views, the transport, and the status bar can all observe one source of truth.
/// </summary>
/// <remarks>
/// All inbound signals are marshalled to the UI thread, so every field is only ever touched there and
/// every event is raised there — subscribers never need their own dispatch.
/// </remarks>
public sealed class EngineWatcher : IDisposable
{
    private readonly Session _session;
    private readonly EngineRpc _engine;

    // Stored handlers so the lambda subscriptions can be detached on dispose, matching the disposal
    // discipline of ViewportStream/AssetCatalog (the session and engine outlive this watcher).
    private readonly Action<ConnectionState> _onStateChanged;
    private readonly Action<bool> _onCompilingChanged;
    private readonly Action _onPlayStarting;
    private readonly Action<bool> _onPlayingChanged;
    private readonly Action<string> _onViewPresented;

    private ConnectionState _connection = ConnectionState.Disconnected;
    private bool _compiling;
    private bool _playing;

    // True from reaching Connected until the first frame is actually presented; keeps us in Loading
    // ("Preparing world…") so the ghost outlives the bare connection.
    private bool _awaitingFirstFrame;

    // True from a play request until it resolves; shows the game-loading phase during the transition.
    private bool _playLoading;

    public EngineWatcher(Session session, EngineRpc engine)
    {
        _session = session;
        _engine = engine;

        _onStateChanged = connection =>
            Dispatch.To(DispatchContext.UI, () => OnConnectionChanged(connection));
        _onCompilingChanged = compiling =>
            Dispatch.To(DispatchContext.UI, () => { _compiling = compiling; Recompute(); });
        _onPlayStarting = () =>
            Dispatch.To(DispatchContext.UI, () => { _playLoading = true; Recompute(); });
        _onPlayingChanged = playing =>
            Dispatch.To(DispatchContext.UI, () => OnPlayingChanged(playing));
        _onViewPresented = _ =>
            Dispatch.To(DispatchContext.UI, OnFramePresented);

        session.StateChanged += _onStateChanged;
        session.CompilingChanged += _onCompilingChanged;
        session.PlayStarting += _onPlayStarting;
        session.PlayingChanged += _onPlayingChanged;
        engine.ViewPresented += _onViewPresented;
    }

    public void Dispose()
    {
        _session.StateChanged -= _onStateChanged;
        _session.CompilingChanged -= _onCompilingChanged;
        _session.PlayStarting -= _onPlayStarting;
        _session.PlayingChanged -= _onPlayingChanged;
        _engine.ViewPresented -= _onViewPresented;
    }

    /// <summary>Raised on the UI thread whenever <see cref="State"/> changes.</summary>
    public event Action<EngineState>? StateChanged;

    /// <summary>
    /// Raised on the UI thread when a <see cref="EngineState.Loading"/> phase resolves into a usable
    /// state (Ready or Playing) — i.e. loading is truly done and a real frame is on screen.
    /// </summary>
    public event Action? LoadCompleted;

    public EngineState State { get; private set; } = EngineState.Off;

    /// <summary>The phase text the loading ghost shows; empty when nothing is loading.</summary>
    public string StatusMessage { get; private set; } = string.Empty;

    /// <summary>
    /// Whether the current <see cref="EngineState.Loading"/> is a play transition (vs a compile/startup
    /// load). Lets the editor viewport stay interactive while only the game viewport shows the ghost.
    /// </summary>
    public bool IsGameLoad { get; private set; }

    private void OnConnectionChanged(ConnectionState connection)
    {
        // A fresh connection has nothing on screen yet; wait for the first presented frame. Any
        // non-connected state clears the in-flight load flags so we don't get wedged.
        if (connection == ConnectionState.Connected)
            _awaitingFirstFrame = true;
        else
        {
            _awaitingFirstFrame = false;
            _playLoading = false;
        }

        _connection = connection;
        Recompute();
    }

    private void OnPlayingChanged(bool playing)
    {
        _playing = playing;
        _playLoading = false;
        Recompute();
    }

    private void OnFramePresented()
    {
        if (!_awaitingFirstFrame)
            return;

        _awaitingFirstFrame = false;
        Recompute();
    }

    private void Recompute()
    {
        var previous = State;
        var (state, message, isGameLoad) = Evaluate();

        IsGameLoad = isGameLoad;
        StatusMessage = message;
        if (state == previous)
            return;

        State = state;
        StateChanged?.Invoke(state);

        // A Loading phase that resolves into a usable state means the load genuinely finished.
        if (previous == EngineState.Loading && state is EngineState.Ready or EngineState.Playing)
            LoadCompleted?.Invoke();
    }

    private (EngineState State, string Message, bool IsGameLoad) Evaluate()
    {
        if (_compiling)
            return (EngineState.Compiling, "Compiling project…", false);

        if (_connection == ConnectionState.Launching)
            return (EngineState.Loading, "Starting engine…", false);

        if (_connection == ConnectionState.Connected)
        {
            if (_awaitingFirstFrame)
                return (EngineState.Loading, "Preparing world…", false);
            if (_playLoading)
                return (EngineState.Loading, "Loading into game…", true);
            return _playing
                ? (EngineState.Playing, string.Empty, false)
                : (EngineState.Ready, string.Empty, false);
        }

        return (EngineState.Off, string.Empty, false);
    }
}
