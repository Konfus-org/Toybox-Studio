using System.Diagnostics;
using Toybox.Studio.Events;
using Toybox.Studio.Input;
using Toybox.Studio.Rpc;
using Toybox.Studio.Utils;

namespace Toybox.Studio.AppHosting;

/// <summary>
/// A peer application the studio owns: a process it launches (or an already-running instance it attaches
/// to) and talks to over a private RPC link. This base holds every generic concept — process lifetime,
/// connect/disconnect, and the messaging primitives — so a concrete owned app (the engine, say) only adds
/// its own vocabulary on top of <see cref="SendCommandAsync(string, object?, CancellationToken)"/> /
/// <see cref="SendNotificationAsync"/> and translates <see cref="StreamInput"/> to its wire format. The RPC
/// transport itself is private: nothing outside this class ever sees it.
///
/// Safety is enforced here so callers don't have to think about it: every command is <b>guarded</b> (any
/// error, including "not connected", becomes a failure <see cref="Result"/> rather than an exception) and
/// <b>bounded</b> (each call is linked to a deadline — the default command timeout unless one is passed —
/// so a hung or crashed peer can never park a caller forever). Notifications carry no reply and so no
/// timeout.
///
/// Signals flow out as typed event structs through the shared <see cref="EventSubscriber.Events"/>
/// dispatcher — never as C# events: the base dispatches the generic <see cref="AppDisconnected"/>/
/// <see cref="AppExited"/> itself, and the owned app dispatches its own structs from its inbound
/// notification handlers. As an <see cref="EventSubscriber"/>, any <see cref="IEventHandler{TEvent}"/>
/// a concrete app implements is registered automatically.
///
/// An <see cref="AppHost{TApp}"/> drives the app-specific lifecycle steps through the seams below
/// (<see cref="Launch"/>, <see cref="GreetAsync"/>, <see cref="RequestShutdownAsync"/>,
/// <see cref="OnDetachingAsync"/>) — the host owns the generic start/attach/stop/restart machinery.
/// </summary>
public abstract class OwnedApp : EventSubscriber, IAsyncDisposable
{
    // Generous enough for a large query, but finite so the studio never blocks indefinitely on a
    // wedged peer.
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(15);

    private readonly RpcClient _rpc = new();
    private Process? _process;

    protected OwnedApp(EventDispatcher events) : base(events)
    {
        _rpc.Disconnected += HandleDisconnected;
    }

    public bool IsConnected => _rpc.IsConnected;

    /// <summary>Whether an owned process is currently running.</summary>
    public bool IsRunning => _process is { HasExited: false };

    /// <summary>The owned process id, or null when none is running (or when attached).</summary>
    public int? ProcessId => _process is { HasExited: false } process ? process.Id : null;

    /// <summary>
    /// Connects to the app's RPC port, retrying while the peer boots, with the owned app's inbound
    /// notification handlers (<see cref="RegisterHandlers"/>) wired before listening starts. Returns a
    /// failure result (never throws) on timeout or error. Any handshake is the owned app's job, on top.
    /// </summary>
    public Task<Result> ConnectAsync(int port, TimeSpan timeout, CancellationToken ct) =>
        _rpc.ConnectAsync(port, timeout, RegisterHandlers, ct);

    /// <summary>Tears down the current connection (no-op if already disconnected).</summary>
    public void Disconnect() => _rpc.Disconnect();

    public virtual async ValueTask DisposeAsync()
    {
        Dispose(); // Unregisters the app's event handlers (the EventSubscriber base).
        await _rpc.DisposeAsync().ContinueOnAnyContext();
        _process?.Dispose();
        _process = null;
    }

    /// <summary>
    /// Sends a command (a method + optional params) and returns a task carrying the outcome as a
    /// <see cref="Result"/>. Await it, poll it, or drop it to fire-and-forget. Bounded by the default
    /// command timeout and guarded, so it never throws and never parks forever. The reply body is
    /// ignored; use <see cref="SendCommandAsync{T}(string, object?, CancellationToken)"/> when you need it.
    /// </summary>
    public Task<Result> SendCommandAsync(string method, object? args = null, CancellationToken ct = default) =>
        SendCommandAsync(method, args, DefaultCommandTimeout, ct);

    /// <summary>Sends a command bounded by <paramref name="timeout"/>: a hung peer cannot park the call past
    /// it. On expiry returns a clear timeout failure; the caller's <paramref name="ct"/> still cancels early.</summary>
    public async Task<Result> SendCommandAsync(string method, object? args, TimeSpan timeout, CancellationToken ct)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(timeout);
        var result = await _rpc.InvokeAsync(method, args, bounded.Token).ContinueOnAnyContext();
        return result.Success || !TimedOut(ct, bounded)
            ? result
            : Result.Fail(TimeoutMessage(method, timeout));
    }

    /// <summary>Sends a command and deserializes its reply into <typeparamref name="T"/>, bounded by the
    /// default command timeout.</summary>
    public Task<Result<T>> SendCommandAsync<T>(string method, object? args = null, CancellationToken ct = default) =>
        SendCommandAsync<T>(method, args, DefaultCommandTimeout, ct);

    /// <summary>Sends a command expecting a typed reply, bounded by <paramref name="timeout"/>.</summary>
    public async Task<Result<T>> SendCommandAsync<T>(
        string method, object? args, TimeSpan timeout, CancellationToken ct)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(timeout);
        var result = await _rpc.InvokeAsync<T>(method, args, bounded.Token).ContinueOnAnyContext();
        return result.Success || !TimedOut(ct, bounded)
            ? result
            : Result<T>.Fail(TimeoutMessage(method, timeout));
    }

    /// <summary>Sends a fire-and-forget notification (no reply awaited); a no-op when not connected. For
    /// high-frequency or best-effort signals — input, selection highlights, log lines.</summary>
    public Task SendNotificationAsync(string method, object? args = null) => _rpc.NotifyAsync(method, args);

    /// <summary>
    /// Streams one input snapshot (fire-and-forget) to the named input surface of the app — e.g. an
    /// engine view. The snapshot speaks the UI framework's typed input vocabulary; the owned app
    /// translates to its own wire format here if needed.
    /// </summary>
    public abstract void StreamInput(string target, InputSnapshot input);

    /// <summary>Waits up to <paramref name="timeout"/> for the owned process to exit; true if it exited.</summary>
    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        var process = _process;
        if (process is null)
            return true;

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token).ContinueOnAnyContext();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Releases the owned process handle: when <paramref name="kill"/> is set and it is still running,
    /// kills the whole tree and waits for it to fully exit so the OS releases its file handles, then
    /// disposes and clears it.
    /// </summary>
    public async Task StopProcessAsync(bool kill, TimeSpan timeout)
    {
        var process = _process;
        _process = null;
        if (process is null)
            return;

        if (kill && !process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token).ContinueOnAnyContext();
            }
            catch (OperationCanceledException)
            {
                // Best-effort; dispose the handle regardless.
            }
        }

        process.Dispose();
    }

    /// <summary>Launches the app's owned process from its launch record, exposing the given RPC port.
    /// The app downcasts to its own <see cref="AppLaunchInfo"/> extension and builds its command line.</summary>
    /// <exception cref="InvalidOperationException">The process could not be started.</exception>
    protected internal abstract void Launch(AppLaunchInfo launch, int rpcPort);

    /// <summary>The app's post-connect handshake (and any connected-session setup). The host tears the
    /// session down on failure. The default has no handshake.</summary>
    protected internal virtual Task<Result> GreetAsync(CancellationToken ct) => Task.FromResult(Result.Ok());

    /// <summary>Asks a connected owned app to exit gracefully (best-effort, bounded); the host kills the
    /// process if it doesn't comply in time. The default asks nothing.</summary>
    protected internal virtual Task RequestShutdownAsync() => Task.CompletedTask;

    /// <summary>Called before detaching from a non-owned app, to leave it in a good standalone state
    /// (e.g. the engine resumes its simulation if the editor paused it). The default does nothing.</summary>
    protected internal virtual Task OnDetachingAsync() => Task.CompletedTask;

    /// <summary>Wires the app's inbound notification handlers, called before the connection starts
    /// listening. Each handler fires on the RPC listener thread — dispatch typed event structs.</summary>
    protected abstract void RegisterHandlers(RpcHandlers handlers);

    /// <summary>The connection dropped (for any reason) — clean up connection-scoped state. The base
    /// dispatches <see cref="AppDisconnected"/> afterwards; the host owns teardown.</summary>
    protected virtual void OnDisconnected()
    {
    }

    /// <summary>
    /// Starts and owns the app's process. Its exit is dispatched as <see cref="AppExited"/>. The
    /// connection itself is made separately via <see cref="ConnectAsync"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The process could not be started.</exception>
    protected void StartProcess(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{startInfo.FileName}'.");
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => Events.Dispatch(new AppExited(process.ExitCode));
        _process = process;
    }

    private void HandleDisconnected()
    {
        OnDisconnected();
        Events.Dispatch(new AppDisconnected());
    }

    // The call expired on OUR deadline (not the caller's cancellation) when the linked source fired but the
    // caller's own token did not — that's a timeout, reported with a clear message instead of the transport's
    // generic "operation canceled".
    private static bool TimedOut(CancellationToken ct, CancellationTokenSource bounded) =>
        bounded.IsCancellationRequested && !ct.IsCancellationRequested;

    private static string TimeoutMessage(string method, TimeSpan timeout) =>
        $"The app did not answer '{method}' within {timeout.TotalSeconds:F0}s.";
}
