using System.Diagnostics;
using System.Globalization;
using Toybox.Studio.Logging;
using Toybox.Studio.Rpc;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi;

/// <summary>The engine's reply to the editor.hello handshake.</summary>
public sealed record Hello(int ProtocolVersion, string Engine, string App);

/// <summary>
/// A <c>view.surface</c> notification: the engine has created (or failed to create) a view's shared
/// GPU texture. <see cref="Handle"/> is a Windows global shared D3D11 texture handle the editor's
/// compositor imports directly; a zero handle means GPU sharing was unavailable for this view.
/// </summary>
public sealed record ViewSurface(string Name, long Handle, int Width, int Height, string Format);

/// <summary>
/// The engine: the launched process AND the editor's connection to it, unified. It owns a generic, engine-agnostic
/// <see cref="RpcClient"/> transport (kept private) and exposes exactly one way to talk to the engine —
/// <see cref="SendCommand(string, object?, CancellationToken)"/> for a request whose reply the caller can await,
/// poll, or ignore, and <see cref="SendNotification"/> for a true fire-and-forget notification. Every domain
/// construct (world / entity / component / asset / settings, the viewport stream, the sync scheduler) builds its
/// engine calls on <see cref="SendCommand"/>; there are no bespoke per-verb methods here. <see cref="Session"/>
/// drives the lifetime — <see cref="Launch"/> (owned) or <see cref="ConnectAsync"/> alone (attach), and teardown.
///
/// Safety is enforced here so callers don't have to think about it: every request is <b>guarded</b> (the transport
/// turns any error, including "not connected", into a failure <see cref="Result"/> rather than an exception) and
/// <b>bounded</b> (each call is linked to a deadline — <see cref="DefaultRequestTimeout"/> unless one is passed — so
/// a hung or crashed engine can never park an editor operation forever). A caller therefore stays safe passing
/// <see cref="CancellationToken.None"/>; it only needs a token to cancel early, and the <c>timeout</c> overloads to
/// pick a tighter or looser bound. Liveness detection (is the engine frozen?) is the <see cref="Session"/>
/// watchdog's job; this layer just guarantees each individual await completes.
/// </summary>
public sealed class Engine : IAsyncDisposable
{
    /// <summary>
    /// The default deadline applied to every request that doesn't pass an explicit one. Generous enough for a
    /// large <c>world.describe</c> or a cold settings/asset describe, but finite so the editor never blocks
    /// indefinitely on an engine that wedged. Notifications (fire-and-forget) carry no reply and so no timeout.
    /// </summary>
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(15);

    private readonly RpcClient _rpc = new();
    private Process? _process;

    public Engine() => _rpc.Disconnected += () => Disconnected?.Invoke();

    /// <summary>Raised for every engine.log notification streamed by the engine.</summary>
    public event Action<LogEntry>? LogReceived;

    /// <summary>
    /// Raised for every view.surface notification: a view's shared GPU texture is ready (or failed).
    /// Each <see cref="ViewportStream"/> filters by view name. Fired on the RPC listener thread.
    /// </summary>
    public event Action<ViewSurface>? SurfaceReceived;

    /// <summary>
    /// Raised for every view.presented notification: a view has drawn its first real frame into the
    /// shared surface (sent once per view, after the surface is created). Carries the view name.
    /// Fired on the RPC listener thread.
    /// </summary>
    public event Action<string>? ViewPresented;

    /// <summary>
    /// Raised for every input.mouseLock notification: the playing game's mouse-lock mode changed
    /// ("unlocked", "relative", or "grabbed"). Lets the game panel capture the cursor for mouselook.
    /// Fired on the RPC listener thread.
    /// </summary>
    public event Action<string>? MouseLockModeChanged;

    /// <summary>
    /// Raised for every view.transformEdited notification: the engine's transform gizmo finished moving one
    /// or more entities, so the editor should mark the world dirty and refresh. Fired on the RPC thread.
    /// </summary>
    public event Action? TransformEdited;

    /// <summary>Raised when the connection drops for any reason.</summary>
    public event Action? Disconnected;

    /// <summary>Raised with the exit code when the owned engine process terminates for any reason.</summary>
    public event Action<int>? Exited;

    public bool IsConnected => _rpc.IsConnected;

    /// <summary>Whether an owned engine process is currently running.</summary>
    public bool IsRunning => _process is { HasExited: false };

    /// <summary>The owned engine process id, or null when none is running (or when attached).</summary>
    public int? ProcessId => _process is { HasExited: false } process ? process.Id : null;

    // --- Connection ---

    /// <summary>
    /// Connects, retrying while the engine boots, then performs the hello handshake. Returns the handshake
    /// on success or a failure result (e.g. on timeout or a failed handshake); never throws.
    /// </summary>
    public async Task<Result<Hello>> ConnectAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        var connected = await _rpc.ConnectAsync(port, timeout, RegisterHandlers, ct).ContinueOnAnyContext();
        if (!connected.Success)
            return Result<Hello>.Fail(connected.Error!);

        var hello = await SendCommand<Hello>(
                EngineMethods.EditorHello, new { ProtocolVersion = 1, Client = "Toybox Studio" }, ct)
            .ContinueOnAnyContext();
        if (!hello.Success)
        {
            _rpc.Disconnect();
            return Result<Hello>.Fail(hello.Error!);
        }

        return Result<Hello>.Ok(hello.Value!);
    }

    /// <summary>Tears down the current connection (no-op if already disconnected).</summary>
    public void Disconnect() => _rpc.Disconnect();

    public async ValueTask DisposeAsync()
    {
        await _rpc.DisposeAsync().ContinueOnAnyContext();
        _process?.Dispose();
        _process = null;
    }

    // --- Commands (the single entry point every engine call flows through) ---

    /// <summary>
    /// Sends a command (an engine RPC method + optional params) and returns a task that carries the outcome as a
    /// <see cref="Result"/>. Await it, save it to poll <see cref="Task.IsCompleted"/> later, or drop it to
    /// fire-and-forget. Bounded by the default request timeout and guarded, so it never throws and never parks
    /// forever. The reply body is ignored; use <see cref="SendCommand{T}(string, object?, CancellationToken)"/>
    /// when you need it.
    /// </summary>
    public Task<Result> SendCommand(string method, object? args = null, CancellationToken ct = default) =>
        SendCommand(method, args, DefaultRequestTimeout, ct);

    /// <summary>Sends a command bounded by <paramref name="timeout"/>: a hung engine cannot park the call past it.
    /// On expiry returns a clear timeout failure rather than the transport's generic "operation canceled"; the
    /// caller's <paramref name="ct"/> still cancels early.</summary>
    public async Task<Result> SendCommand(string method, object? args, TimeSpan timeout, CancellationToken ct)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(timeout);
        var result = await _rpc.InvokeAsync(method, args, bounded.Token).ContinueOnAnyContext();
        return result.Success || !TimedOut(ct, bounded)
            ? result
            : Result.Fail(TimeoutMessage(method, timeout));
    }

    /// <summary>Sends a command and deserializes its reply into <typeparamref name="T"/>, bounded by the default
    /// request timeout. The reply-carrying counterpart to <see cref="SendCommand(string, object?, CancellationToken)"/>.</summary>
    public Task<Result<T>> SendCommand<T>(string method, object? args = null, CancellationToken ct = default) =>
        SendCommand<T>(method, args, DefaultRequestTimeout, ct);

    /// <summary>Sends a command expecting a typed reply, bounded by <paramref name="timeout"/>.</summary>
    public async Task<Result<T>> SendCommand<T>(
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
    /// high-frequency or best-effort signals — viewport input, selection highlight, the gizmo mode, log lines.</summary>
    public Task SendNotification(string method, object? args = null) => _rpc.NotifyAsync(method, args);

    // --- Process lifetime (owned launches) ---

    /// <summary>
    /// Launches and owns an engine process: starts the launcher with the RPC port exposed via
    /// TBX_STUDIO_RPC_PORT, hosting the given app module with the given settings file, and injects the studio
    /// bridge plugin. Ties the engine's lifetime to ours so a hard studio crash never orphans it. Wires the
    /// process's exit to <see cref="Exited"/>. The connection itself is made separately via <see cref="ConnectAsync"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The launcher could not be started.</exception>
    public void Launch(
        string launcherPath,
        string appModuleName,
        string appSettingsPath,
        bool hidden,
        int rpcPort,
        string? extraAssetDirectory = null)
    {
        if (!File.Exists(launcherPath))
            throw new InvalidOperationException($"Engine launcher not found at '{launcherPath}'.");

        var startInfo = new ProcessStartInfo(launcherPath)
        {
            WorkingDirectory = Path.GetDirectoryName(launcherPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment["TBX_STUDIO_RPC_PORT"] = rpcPort.ToString(CultureInfo.InvariantCulture);

        startInfo.ArgumentList.Add($"--app={appModuleName}");
        startInfo.ArgumentList.Add($"--settings={appSettingsPath}");

        if (hidden)
            startInfo.ArgumentList.Add("--hidden");

        // Inject the studio bridge plugin (added on top of the project's own plugins). It owns all
        // editor behavior — the RPC server, viewport rendering, and play-mode — so the engine itself
        // stays a clean game runtime. Standalone launches omit this and never load any editor code.
        startInfo.ArgumentList.Add("--inject-plugins=StudioBridge");

        // Tie the engine's lifetime to ours: if the studio dies (even by a hard crash, where no
        // managed teardown runs), the engine sees our process exit and shuts itself down instead
        // of lingering as an orphan that holds the in-tree engine binaries locked. Owned launches
        // only — StartStandalone deliberately omits this so shipped builds run independently.
        startInfo.ArgumentList.Add(
            $"--live-together-die-together={Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}");

        // Give the engine an extra asset root (the editor's bundled asset-viewer sky/world) so asset
        // previews can load it regardless of which project is open. Owned launches only.
        if (!string.IsNullOrEmpty(extraAssetDirectory) && Directory.Exists(extraAssetDirectory))
            startInfo.ArgumentList.Add($"--register-assets={extraAssetDirectory}");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{launcherPath}'.");
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => Exited?.Invoke(process.ExitCode);
        _process = process;
    }

    /// <summary>
    /// Starts a launcher in standalone mode: a visible window, no editor RPC port, and not owned by
    /// any studio session. Used to run an exported Release build for the user to test independently.
    /// </summary>
    public static void StartStandalone(string launcherPath, string appModuleName, string appSettingsPath)
    {
        if (!File.Exists(launcherPath))
            throw new InvalidOperationException($"Engine launcher not found at '{launcherPath}'.");

        var startInfo = new ProcessStartInfo(launcherPath)
        {
            WorkingDirectory = Path.GetDirectoryName(launcherPath)!,
            UseShellExecute = true,
        };
        startInfo.ArgumentList.Add($"--app={appModuleName}");
        startInfo.ArgumentList.Add($"--settings={appSettingsPath}");

        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException($"Failed to start '{launcherPath}'.");
    }

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
    /// Releases the owned process handle: when <paramref name="kill"/> is set and it is still running, kills the
    /// whole tree and waits for it to fully exit so the OS releases its file handles — notably on the engine
    /// binaries, which are built in-tree and relinked on the next compile — then disposes and clears it.
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

    // The call expired on OUR deadline (not the caller's cancellation) when the linked source fired but the
    // caller's own token did not — that's a timeout, reported with a clear message instead of the transport's
    // generic "operation canceled".
    private static bool TimedOut(CancellationToken ct, CancellationTokenSource bounded) =>
        bounded.IsCancellationRequested && !ct.IsCancellationRequested;

    private static string TimeoutMessage(string method, TimeSpan timeout) =>
        $"The engine did not answer '{method}' within {timeout.TotalSeconds:F0}s.";

    // The engine's inbound notification handlers, wired before the connection starts listening. Re-raised as
    // typed events; each fires on the RPC listener thread.
    private void RegisterHandlers(RpcHandlers handlers)
    {
        handlers.On(
            EngineMethods.EngineLog,
            (string level, string message) =>
                LogReceived?.Invoke(new LogEntry(LogLevels.Parse(level), message)));
        handlers.On(
            EngineMethods.ViewSurface,
            (string name, long sharedHandle, int width, int height, string format) =>
                SurfaceReceived?.Invoke(new ViewSurface(name, sharedHandle, width, height, format)));
        handlers.On(EngineMethods.ViewPresented, (string name) => ViewPresented?.Invoke(name));
        handlers.On("input.mouseLock", (string mode) => MouseLockModeChanged?.Invoke(mode));
        handlers.On(EngineMethods.ViewTransformEdited, (ulong[] ids) => TransformEdited?.Invoke());
    }
}
