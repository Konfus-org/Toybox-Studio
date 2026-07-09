using System.Diagnostics;
using System.Globalization;
using Toybox.Studio.AppHosting;
using Toybox.Studio.Events;
using Toybox.Studio.Input;
using Toybox.Studio.Logging;
using Toybox.Studio.Rpc;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi;

/// <summary>The engine's reply to the editor.hello handshake.</summary>
public sealed record Hello(int ProtocolVersion, string Engine, string App);

/// <summary>
/// Everything one engine launch needs — handed to <c>AppHost&lt;Engine&gt;.StartAsync</c> by whoever
/// coordinates the session: the launcher the project's build produced, the app module + settings it
/// hosts, and the launch policy.
/// </summary>
public sealed record EngineLaunchInfo(
    string LauncherPath,
    string ModuleName,
    string AppSettingsPath,
    bool HideWindow = true) : AppLaunchInfo
{
    public override string Name => ModuleName;
}

/// <summary>
/// The engine as an <see cref="OwnedApp"/>, hosted by an <c>AppHost&lt;Engine&gt;</c>. The generic
/// concepts — process lifetime, connect/disconnect, bounded commands/notifications, input streaming —
/// all live in the base and the host; this class is everything engine-specific: the launcher command
/// line, the hello handshake (plus wiring studio logs into the engine console), the run-state door
/// (<see cref="SetStateAsync"/>: play/pause/edit/shutdown with valid transitions enforced), the
/// translation of <see cref="InputSnapshot"/> to the engine's input wire format, and the derived
/// <see cref="State"/> — anything holding the engine service can just read <c>engine.State</c>.
/// </summary>
public sealed class Engine : OwnedApp,
    IEventHandler<ConnectionChanged>,
    IEventHandler<BuildStateChanged>
{
    /// <summary>The well-known RPC port a standalone-running engine listens on (for attach detection).</summary>
    public const int DefaultPort = 17890;

    private readonly Logger _log;

    // The low-level signals State is derived from. Touched only on the UI thread (every inbound signal
    // marshals there first), so derivation never races and subscribers never need their own dispatch.
    private ConnectionState _connection = ConnectionState.Disconnected;
    private HostKind _kind = HostKind.None;
    private bool _compiling;
    private bool _playing;
    private bool _paused;

    // True from reaching Connected until the first frame is actually presented; keeps State in Loading
    // ("Preparing world…") so the ghost outlives the bare connection.
    private bool _awaitingFirstFrame;

    // True from a play request until it resolves; shows the game-loading phase during the transition.
    private bool _playLoading;

    public Engine(Logger log, EventDispatcher events) : base(events) => _log = log;

    /// <summary>
    /// The single "what is the engine doing right now" value, folded from the host's connection signals,
    /// the project's compile phase, the run-state transitions, and the engine's own first-presented-frame
    /// notification. Updated on the UI thread; every change also dispatches
    /// <see cref="EngineStateChanged"/> there.
    /// </summary>
    public EngineState State { get; private set; } = EngineState.Off;

    // The run state as State reflects it. Command paths read this from non-UI threads, so a snapshot
    // taken mid-transition can be momentarily stale — at worst an idempotent set is re-sent.
    private EngineRunState RunState =>
        State.IsPaused ? EngineRunState.Paused
        : State.Phase == EnginePhase.Playing ? EngineRunState.Playing
        : EngineRunState.Editing;

    public void Handle(in ConnectionChanged evt)
    {
        var (connection, kind) = (evt.State, evt.Kind);
        Dispatch.To(DispatchContext.UI, () => OnConnectionChanged(connection, kind));
    }

    public void Handle(in BuildStateChanged evt)
    {
        var compiling = evt.IsBuilding;
        Dispatch.To(DispatchContext.UI, () => { _compiling = compiling; RecomputeState(); });
    }

    /// <summary>
    /// Asks the engine to enter a run state — the one door for everything an external caller may change
    /// about the engine's execution: play (<see cref="EngineRunState.Playing"/> snapshots the world and
    /// starts the game loop, or resumes from a pause), pause, back to editing (restores the pre-play
    /// world without stopping the engine or the viewports), or a graceful shutdown. Rejects what the
    /// engine can't honor: any request while disconnected, and any transition the run-state machine
    /// doesn't allow (e.g. pausing while not playing). Requesting the current state succeeds as a no-op.
    /// </summary>
    public async Task<Result> SetStateAsync(EngineRunState state)
    {
        if (!IsConnected)
            return Result.Fail("The engine is not connected.");

        var current = RunState;
        if (state == current)
            return Result.Ok();

        if (!IsValidTransition(current, state))
            return Result.Fail($"The engine cannot go from {current} to {state}.");

        return state switch
        {
            // Playing is reached two ways: resuming a paused game, or entering play mode from editing.
            EngineRunState.Playing when current == EngineRunState.Paused =>
                await SetPausedAsync(false).ContinueOnAnyContext(),
            EngineRunState.Playing => await SetPlayingAsync(true).ContinueOnAnyContext(),
            EngineRunState.Paused => await SetPausedAsync(true).ContinueOnAnyContext(),
            EngineRunState.Editing => await SetPlayingAsync(false).ContinueOnAnyContext(),
            _ => await ShutdownAsync().ContinueOnAnyContext(),
        };
    }

    /// <summary>
    /// Streams one input snapshot to an engine view, translating the UI framework's typed input into
    /// the engine's wire format: buttons and fly-camera move keys become bitmasks, held keys become
    /// engine <see cref="InputKey"/> codes, and pointer values pass through. Mouse/wheel values are
    /// deltas since the last snapshot.
    /// </summary>
    public override void StreamInput(string target, InputSnapshot input)
    {
        if (!IsConnected)
            return;

        var wire = EngineInputTranslator.Translate(input);
        SendNotificationAsync(
            EngineCommands.ViewInput,
            new
            {
                View = target,
                Focused = input.Focused,
                Buttons = wire.Buttons,
                MoveKeys = wire.MoveKeys,
                Keys = wire.Keys,
                MouseX = input.PointerPosition.X,
                MouseY = input.PointerPosition.Y,
                Dx = input.PointerDelta.X,
                Dy = input.PointerDelta.Y,
                Wheel = input.WheelDelta,
                CursorU = input.NormalizedPointer.X,
                CursorV = input.NormalizedPointer.Y,
            });
    }

    /// <summary>
    /// The editor.hello handshake; on success, studio log lines start flowing into the engine's unified
    /// log and the engine console's colors track the editor theme.
    /// </summary>
    protected override async Task<Result> GreetAsync(CancellationToken ct)
    {
        var hello = await SendCommandAsync<Hello>(
                EngineCommands.EditorHello, new { ProtocolVersion = 1, Client = "Toybox Studio" }, ct)
            .ContinueOnAnyContext();
        if (hello is not { Success: true, Value: { } greeting })
            return Result.Fail(hello.Error ?? "The engine sent no hello reply.");

        _log.SetEngineForwarder((level, message) =>
            SendNotificationAsync(EngineCommands.EditorLog, new { Level = level, Message = message }));
        _log.SetLogColorSink((info, warning, error, ct2) =>
            SendCommandAsync(
                EngineCommands.EngineSetLogColors, new { Info = info, Warning = warning, Error = error }, ct2));
        _log.Info(
            $"Connected to {greeting.Engine} (app '{greeting.App}', protocol v{greeting.ProtocolVersion}).");
        return Result.Ok();
    }

    /// <summary>
    /// Launches an engine process: starts the launcher with the RPC port exposed via the environment,
    /// hosting the given app module with the given settings file, and injects the studio bridge plugin.
    /// Ties the engine's lifetime to ours so a hard studio crash never orphans it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The launcher could not be started.</exception>
    protected override void Launch(AppLaunchInfo launch, int rpcPort)
    {
        if (launch is not EngineLaunchInfo engineLaunch)
            throw new InvalidOperationException($"The engine launches from an {nameof(EngineLaunchInfo)}.");

        if (!File.Exists(engineLaunch.LauncherPath))
            throw new InvalidOperationException($"Engine launcher not found at '{engineLaunch.LauncherPath}'.");

        var startInfo = new ProcessStartInfo(engineLaunch.LauncherPath)
        {
            WorkingDirectory = Path.GetDirectoryName(engineLaunch.LauncherPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment[EngineCommands.RpcPortVariable] = rpcPort.ToString(CultureInfo.InvariantCulture);

        startInfo.ArgumentList.Add($"{EngineCommands.AppArgument}={engineLaunch.ModuleName}");
        startInfo.ArgumentList.Add($"{EngineCommands.SettingsArgument}={engineLaunch.AppSettingsPath}");

        if (engineLaunch.HideWindow)
            startInfo.ArgumentList.Add(EngineCommands.HiddenArgument);

        // Inject the studio bridge plugin (added on top of the project's own plugins). It owns all
        // editor behavior — the RPC server, viewport rendering, and play-mode — so the engine itself
        // stays a clean game runtime. Standalone launches omit this and never load any editor code.
        startInfo.ArgumentList.Add($"{EngineCommands.InjectPluginsArgument}=StudioBridge");

        // Tie the engine's lifetime to ours: if the studio dies (even by a hard crash, where no
        // managed teardown runs), the engine sees our process exit and shuts itself down instead
        // of lingering as an orphan that holds the in-tree engine binaries locked.
        startInfo.ArgumentList.Add(
            $"{EngineCommands.LifetimeLinkArgument}={Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}");

        StartProcess(startInfo);
    }

    /// <summary>A detached engine keeps running on its own, so leave its simulation unpaused.</summary>
    protected override Task OnDetachingAsync() =>
        State.IsPaused ? SetPausedAsync(false) : Task.CompletedTask;

    /// <summary>Asks the engine to exit via the engine.shutdown RPC (bounded; best-effort).</summary>
    protected override Task RequestShutdownAsync() => ShutdownAsync();

    /// <summary>The connection is gone: stop forwarding studio logs into it. The state fold resets the
    /// run flags itself when the connection change arrives.</summary>
    protected override void OnDisconnected()
    {
        _log.SetEngineForwarder(null);
        _log.SetLogColorSink(null);
    }

    // The engine's inbound notifications (each fires on the RPC listener thread): log lines flow into
    // the unified studio log directly; the rest re-dispatch as typed event structs or feed State.
    protected override void RegisterHandlers(RpcHandlers handlers)
    {
        handlers.On(
            EngineCommands.EngineLog,
            (string level, string message) => _log.Log(LogLevels.Parse(level), message));
        handlers.On(
            EngineCommands.ViewSurface,
            (string name, long sharedHandle, int width, int height, string format) =>
                Events.Dispatch(new ViewSurfaceCreated(name, sharedHandle, width, height, format)));
        handlers.On(
            EngineCommands.ViewPresented,
            (string name) => Dispatch.To(DispatchContext.UI, OnFramePresented));
        handlers.On(
            EngineCommands.SyncChanged,
            (string address, string key, Newtonsoft.Json.Linq.JToken value) =>
                Events.Dispatch(new SyncChanged(address, key, value)));
        handlers.On(
            EngineCommands.SyncEvent,
            (string address, string key, Newtonsoft.Json.Linq.JToken args) =>
                Events.Dispatch(new SyncEventRaised(address, key, args)));
    }

    // The allowed run-state transitions: play from editing, pause only while playing, resume or stop
    // from paused, and a graceful shutdown from anywhere.
    private static bool IsValidTransition(EngineRunState from, EngineRunState to) => (from, to) switch
    {
        (EngineRunState.Editing, EngineRunState.Playing) => true,
        (EngineRunState.Playing, EngineRunState.Paused) => true,
        (EngineRunState.Playing, EngineRunState.Editing) => true,
        (EngineRunState.Paused, EngineRunState.Playing) => true,
        (EngineRunState.Paused, EngineRunState.Editing) => true,
        (_, EngineRunState.Shutdown) => true,
        _ => false,
    };

    private EngineState Evaluate()
    {
        if (_compiling)
            return new EngineState(EnginePhase.Compiling, _kind, "Compiling project…");

        if (_connection == ConnectionState.Launching)
            return new EngineState(EnginePhase.Loading, _kind, "Starting engine…");

        if (_connection == ConnectionState.Connected)
        {
            if (_awaitingFirstFrame)
                return new EngineState(EnginePhase.Loading, _kind, "Preparing world…");
            if (_playLoading)
                return new EngineState(EnginePhase.Loading, _kind, "Loading into game…", IsGameLoading: true);
            return new EngineState(_playing ? EnginePhase.Playing : EnginePhase.Ready, _kind, IsPaused: _paused);
        }

        return EngineState.Off;
    }

    private void OnConnectionChanged(ConnectionState connection, HostKind kind)
    {
        // A fresh connection has nothing on screen yet; wait for the first presented frame. Any
        // non-connected state resets the run flags so a later connection starts clean.
        if (connection == ConnectionState.Connected)
            _awaitingFirstFrame = true;
        else
        {
            _awaitingFirstFrame = false;
            _playLoading = false;
            _playing = false;
            _paused = false;
        }

        _connection = connection;
        _kind = kind;
        RecomputeState();
    }

    private void OnFramePresented()
    {
        if (!_awaitingFirstFrame)
            return;

        _awaitingFirstFrame = false;
        RecomputeState();
    }

    // Value equality on the struct covers every change worth announcing — including a message-only
    // change (e.g. "Starting engine…" → "Preparing world…", both Loading) so the ghost text stays current.
    private void RecomputeState()
    {
        var state = Evaluate();
        if (state == State)
            return;

        State = state;
        Events.Dispatch(new EngineStateChanged(state));
    }

    private async Task<Result> SetPausedAsync(bool isPaused)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await SendCommandAsync(
                EngineCommands.EngineSetPaused, new { IsPaused = isPaused }, cts.Token)
            .ContinueOnAnyContext();
        if (!result.Success)
        {
            _log.Error($"Pause request failed: {result.Error}");
            return result;
        }

        Dispatch.To(DispatchContext.UI, () => { _paused = isPaused; RecomputeState(); });
        _log.Info(isPaused ? "Engine paused." : "Engine resumed.");
        return Result.Ok();
    }

    private async Task<Result> SetPlayingAsync(bool isPlaying)
    {
        // Show the game-loading phase before the round-trip so State reflects the transition immediately.
        if (isPlaying)
            Dispatch.To(DispatchContext.UI, () => { _playLoading = true; RecomputeState(); });

        // Leaving play clears any pause so the next play session starts clean.
        if (!isPlaying && State.IsPaused)
            await SetPausedAsync(false).ContinueOnAnyContext();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await SendCommandAsync(
                EngineCommands.EngineSetPlaying, new { IsPlaying = isPlaying }, cts.Token)
            .ContinueOnAnyContext();
        if (!result.Success)
        {
            _log.Error($"{(isPlaying ? "Play" : "Stop")} request failed: {result.Error}");
            // The transition failed: leave the game-loading phase instead of waiting on it forever.
            if (isPlaying)
                Dispatch.To(DispatchContext.UI, () => { _playLoading = false; RecomputeState(); });
            return result;
        }

        Dispatch.To(DispatchContext.UI, () => { _playing = isPlaying; _playLoading = false; RecomputeState(); });
        _log.Info(isPlaying ? "Game started." : "Game stopped.");
        return Result.Ok();
    }

    private async Task<Result> ShutdownAsync()
    {
        // Best-effort: the engine may drop the connection before replying; the host's process wait
        // decides whether a kill is still needed.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        return await SendCommandAsync(EngineCommands.EngineShutdown, null, cts.Token).ContinueOnAnyContext();
    }
}
