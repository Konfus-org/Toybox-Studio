using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;
using Toybox.Studio.Logging;
using Toybox.Studio.Project;

namespace Toybox.Studio.EngineApi;

/// <summary>The engine's reply to the editor.hello handshake.</summary>
public sealed record Hello(int ProtocolVersion, string Engine, string App);

/// <summary>The engine's reply to view.start: the new view's id and pixel format.</summary>
public sealed record ViewInfo(string Name, string Format);

/// <summary>
/// A <c>view.surface</c> notification: the engine has created (or failed to create) a view's shared
/// GPU texture. <see cref="Handle"/> is a Windows global shared D3D11 texture handle the editor's
/// compositor imports directly; a zero handle means GPU sharing was unavailable for this view.
/// </summary>
public sealed record ViewSurface(string Name, long Handle, int Width, int Height, string Format);

/// <summary>
/// The one, always-injectable engine API. It owns the JSON-RPC connection to the engine's StudioBridge
/// plugin (newline-delimited JSON over loopback TCP) and exposes every call as an easy-to-use, transactional
/// wrapper that returns a <see cref="Result"/> with a helpful message on failure rather than throwing —
/// including returning failure when not connected. <see cref="Session"/> drives the connection
/// (<see cref="ConnectAsync"/>/<see cref="Disconnect"/>); everyone else just calls the wrappers.
/// </summary>
public sealed class EngineRpc : IAsyncDisposable
{
    private const string NotConnected = "Not connected to the engine.";

    private TcpClient? _tcpClient;
    private JsonRpc? _rpc;

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

    /// <summary>Raised when the connection drops for any reason.</summary>
    public event Action? Disconnected;

    public bool IsConnected => _rpc is { IsDisposed: false };

    /// <summary>
    /// Connects, retrying while the engine boots, then performs the hello handshake. Returns the handshake
    /// on success or a failure result (e.g. on timeout); never throws.
    /// </summary>
    public async Task<Result<Hello>> ConnectAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            _tcpClient = await ConnectWithRetryAsync(port, timeout, ct).ContinueOnAnyContext();

            var formatter = new JsonMessageFormatter();
            formatter.JsonSerializer.ContractResolver = new CamelCasePropertyNamesContractResolver();

            var stream = _tcpClient.GetStream();
            _rpc = new JsonRpc(new NewLineDelimitedMessageHandler(stream, stream, formatter));
            _rpc.AddLocalRpcMethod(
                "engine.log",
                (string level, string message) =>
                    LogReceived?.Invoke(new LogEntry(LogLevels.Parse(level), message)));
            _rpc.AddLocalRpcMethod(
                "view.surface",
                (string name, long sharedHandle, int width, int height, string format) =>
                    SurfaceReceived?.Invoke(new ViewSurface(name, sharedHandle, width, height, format)));
            _rpc.AddLocalRpcMethod(
                "view.presented",
                (string name) => ViewPresented?.Invoke(name));
            _rpc.AddLocalRpcMethod(
                "input.mouseLock",
                (string mode) => MouseLockModeChanged?.Invoke(mode));
            _rpc.Disconnected += (_, _) => Disconnected?.Invoke();
            _rpc.StartListening();

            var hello = await _rpc
                .InvokeWithParameterObjectAsync<Hello>(
                    "editor.hello",
                    new { ProtocolVersion = 1, Client = "Toybox Studio" },
                    ct).ContinueOnAnyContext();
            return Result<Hello>.Ok(hello);
        }
        catch (Exception exception)
        {
            Disconnect();
            return Result<Hello>.Fail(exception.Message);
        }
    }

    /// <summary>Tears down the current connection (no-op if already disconnected).</summary>
    public void Disconnect()
    {
        _rpc?.Dispose();
        _rpc = null;
        _tcpClient?.Dispose();
        _tcpClient = null;
    }

    public ValueTask DisposeAsync()
    {
        Disconnect();
        return ValueTask.CompletedTask;
    }

    /// <summary>Round-trips an <c>engine.ping</c> and, on success, reports the measured latency.</summary>
    public async Task<Result<TimeSpan>> PingAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await InvokeAsync("engine.ping", null, ct).ContinueOnAnyContext();
        return result.Success
            ? Result<TimeSpan>.Ok(stopwatch.Elapsed)
            : Result<TimeSpan>.Fail(result.Error!);
    }

    /// <summary>Pauses or resumes the engine's simulation; rendering keeps running.</summary>
    public Task<Result> SetPausedAsync(bool isPaused, CancellationToken ct) =>
        InvokeAsync("engine.setPaused", new { IsPaused = isPaused }, ct);

    /// <summary>
    /// Enters or exits play mode. Entering snapshots the world and runs the game loop; exiting
    /// restores the pre-play world and halts simulation. The engine keeps rendering either way.
    /// </summary>
    public Task<Result> SetPlayingAsync(bool isPlaying, CancellationToken ct) =>
        InvokeAsync("engine.setPlaying", new { IsPlaying = isPlaying }, ct);

    /// <summary>Asks the engine to exit gracefully.</summary>
    public Task<Result> ShutdownAsync(CancellationToken ct) =>
        InvokeAsync("engine.shutdown", null, ct);

    /// <summary>
    /// Pushes the editor's log colors to the engine console. Best-effort cosmetic sync, so it returns a bare
    /// task the logger can fire and forget.
    /// </summary>
    public Task SetLogColorsAsync(string info, string warning, string error, CancellationToken ct) =>
        InvokeAsync("engine.setLogColors", new { Info = info, Warning = warning, Error = error }, ct);

    /// <summary>Pushes one editor-originated log line into the engine's unified log (fire-and-forget).</summary>
    public Task WriteLogAsync(string level, string message) =>
        NotifyAsync("editor.log", new { Level = level, Message = message });

    /// <summary>Fetches the active world's entities with their reflected component data (the raw reply).</summary>
    public Task<Result<JObject>> DescribeWorldAsync(CancellationToken ct) =>
        InvokeAsync<JObject>("world.describe", null, ct);

    /// <summary>
    /// Fetches one entity's current reflected component data (the same per-entity shape as
    /// <see cref="DescribeWorldAsync"/>, wrapped as <c>{ entity, component_types }</c>). Used to keep the
    /// selected entity in sync with the running game without re-describing the whole world.
    /// </summary>
    public Task<Result<JObject>> DescribeEntityAsync(ulong entityId, CancellationToken ct) =>
        InvokeAsync<JObject>("entity.describe", new { EntityId = entityId }, ct);

    /// <summary>
    /// Creates a new entity (optionally named and parented; a zero/omitted parent means a root entity) and
    /// returns its new id. The engine appends it after its last sibling.
    /// </summary>
    public async Task<Result<ulong>> CreateEntityAsync(string? name, ulong parent, CancellationToken ct)
    {
        var result = await InvokeAsync<JObject>(
            "entity.create",
            new { Name = name ?? "", Parent = parent },
            ct).ContinueOnAnyContext();
        return result is { Success: true, Value: { } reply }
            ? Result<ulong>.Ok(reply.Value<ulong>("id"))
            : Result<ulong>.Fail(result.Error ?? "The engine returned no result.");
    }

    /// <summary>Destroys an entity and its whole subtree.</summary>
    public Task<Result> DestroyEntityAsync(ulong entityId, CancellationToken ct) =>
        InvokeAsync("entity.destroy", new { EntityId = entityId }, ct);

    /// <summary>Promotes or demotes an entity between global (full-lifetime resident) and ordinary scene.</summary>
    public Task<Result> SetEntityGlobalAsync(ulong entityId, bool global, CancellationToken ct) =>
        InvokeAsync("entity.setGlobal", new { EntityId = entityId, Global = global }, ct);

    /// <summary>
    /// Moves an entity to <paramref name="parent"/> (zero = root) and to position <paramref name="index"/>
    /// among that parent's children — one call covers both reorder and reparent.
    /// </summary>
    public Task<Result> MoveEntityAsync(ulong entityId, ulong parent, int index, CancellationToken ct) =>
        InvokeAsync(
            "entity.move",
            new { EntityId = entityId, Parent = parent, Index = index },
            ct);

    /// <summary>Replaces a whole component on an entity with the given typed JSON.</summary>
    public Task<Result> SetComponentAsync(ulong entityId, string component, JObject value, CancellationToken ct) =>
        InvokeAsync(
            "entity.setComponent",
            new { EntityId = entityId, Component = component, Value = value },
            ct);

    /// <summary>Reads one component property as a self-describing <c>{ type, value }</c> node.</summary>
    public Task<Result<JObject>> GetPropertyAsync(
        ulong entityId, string component, string property, CancellationToken ct) =>
        InvokeAsync<JObject>(
            "reflect.get",
            new { EntityId = entityId, Component = component, Property = property },
            ct);

    /// <summary>Writes one component property in place from its bare serialized value.</summary>
    public Task<Result> SetPropertyAsync(
        ulong entityId, string component, string property, JToken value, CancellationToken ct) =>
        InvokeAsync(
            "reflect.set",
            new { EntityId = entityId, Component = component, Property = property, Value = value },
            ct);

    /// <summary>Resets one component property to its default value.</summary>
    public Task<Result> ResetPropertyAsync(
        ulong entityId, string component, string property, CancellationToken ct) =>
        InvokeAsync(
            "reflect.reset",
            new { EntityId = entityId, Component = component, Property = property },
            ct);

    /// <summary>Asks whether one component property currently equals its default value.</summary>
    public async Task<Result<bool>> IsPropertyDefaultAsync(
        ulong entityId, string component, string property, CancellationToken ct)
    {
        var result = await InvokeAsync<JObject>(
            "reflect.isDefault",
            new { EntityId = entityId, Component = component, Property = property },
            ct).ContinueOnAnyContext();
        return result is { Success: true, Value: { } reply }
            ? Result<bool>.Ok(reply.Value<bool>("isDefault"))
            : Result<bool>.Fail(result.Error ?? "The engine returned no result.");
    }

    /// <summary>
    /// Asks the engine to start a new view and returns its unique id. Each call creates an
    /// independent view with its own engine camera: an editor camera (free, spawned at the game
    /// camera) or a game view that mirrors the game camera. The view's shared GPU texture arrives
    /// shortly after as a <see cref="SurfaceReceived"/> notification (created on the render lane).
    /// </summary>
    public Task<Result<ViewInfo>> StartViewAsync(ViewKind kind, CancellationToken ct) =>
        InvokeAsync<ViewInfo>("view.start", new { Kind = kind == ViewKind.Game ? "game" : "editor" }, ct);

    /// <summary>Stops the engine view with the given id (from <see cref="StartViewAsync"/>).</summary>
    public Task<Result> StopViewAsync(string name, CancellationToken ct) =>
        InvokeAsync("view.stop", new { Name = name }, ct);

    /// <summary>
    /// Forwards the focused viewport's input to its engine view (fire-and-forget notification). Drives
    /// the editor fly camera; mouse/wheel values are deltas since the last call. Buttons: bit0 left,
    /// bit1 right, bit2 middle. MoveKeys: bit0 fwd, 1 back, 2 left, 3 right, 4 up, 5 down.
    /// </summary>
    public Task SendViewInputAsync(
        string view, bool focused, int buttons, int moveKeys,
        IReadOnlyList<int> keys, double mouseX, double mouseY, double dx, double dy, double wheel) =>
        NotifyAsync(
            "view.input",
            new
            {
                View = view, Focused = focused, Buttons = buttons, MoveKeys = moveKeys,
                Keys = keys, MouseX = mouseX, MouseY = mouseY, Dx = dx, Dy = dy, Wheel = wheel,
            });

    /// <summary>Fetches the project's assets and registered script types.</summary>
    public Task<Result<AssetCatalogReply>> ListAssetsAsync(CancellationToken ct) =>
        InvokeAsync<AssetCatalogReply>("editor.listAssets", null, ct);

    public Task<Result> SaveWorldAsync(string path, CancellationToken ct) =>
        Task.FromResult(Result.Fail(
            "Saving worlds is not supported yet (pending the engine's world.save RPC)."));

    public Task<Result> LoadWorldAsync(string path, CancellationToken ct) =>
        Task.FromResult(Result.Fail(
            "Loading worlds is not supported yet (pending the engine's world.load RPC)."));

    private async Task<Result<T>> InvokeAsync<T>(string method, object? args, CancellationToken ct)
    {
        var rpc = _rpc;
        if (rpc is null || rpc.IsDisposed)
            return Result<T>.Fail(NotConnected);

        try
        {
            var value = args is null
                ? await rpc.InvokeWithCancellationAsync<T>(method, null, ct).ContinueOnAnyContext()
                : await rpc.InvokeWithParameterObjectAsync<T>(method, args, ct).ContinueOnAnyContext();
            return Result<T>.Ok(value);
        }
        catch (Exception exception)
        {
            return Result<T>.Fail(exception.Message);
        }
    }

    private async Task<Result> InvokeAsync(string method, object? args, CancellationToken ct)
    {
        var rpc = _rpc;
        if (rpc is null || rpc.IsDisposed)
            return Result.Fail(NotConnected);

        try
        {
            if (args is null)
                await rpc.InvokeWithCancellationAsync(method, null, ct).ContinueOnAnyContext();
            else
                await rpc.InvokeWithParameterObjectAsync(method, args, ct).ContinueOnAnyContext();
            return Result.Ok();
        }
        catch (Exception exception)
        {
            return Result.Fail(exception.Message);
        }
    }

    private Task NotifyAsync(string method, object? args)
    {
        var rpc = _rpc;
        if (rpc is null || rpc.IsDisposed)
            return Task.CompletedTask;

        return args is null ? rpc.NotifyAsync(method) : rpc.NotifyWithParameterObjectAsync(method, args);
    }

    private static async Task<TcpClient> ConnectWithRetryAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var tcpClient = new TcpClient();
            try
            {
                await tcpClient.ConnectAsync(IPAddress.Loopback, port, ct).ContinueOnAnyContext();
                return tcpClient;
            }
            catch (SocketException)
            {
                tcpClient.Dispose();
                if (deadline.Elapsed > timeout)
                    throw new TimeoutException($"Engine did not accept an RPC connection on port {port} within {timeout.TotalSeconds:F0}s.");

                await Task.Delay(TimeSpan.FromMilliseconds(250), ct).ContinueOnAnyContext();
            }
        }
    }
}
