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

/// <summary>The engine's reply to view.start: where and how frames are shared.</summary>
public sealed record ViewInfo(string Name, string Format);

/// <summary>
/// One reflected property's metadata from <c>reflect.describeType</c>: its serialization type token plus
/// the editor attributes and, for composite properties, the wire name of the nested type.
/// </summary>
public sealed record ReflectedProperty(
    string Name,
    string Type,
    string? NestedType,
    string? Category,
    string? Description,
    string? View,
    bool Readonly,
    bool Hidden);

/// <summary>A reflected type's editor description from <c>reflect.describeType</c>.</summary>
public sealed record ReflectedType(string Name, string? Icon, string? IconColor, List<ReflectedProperty> Properties);

/// <summary>The <c>reflect.isDefault</c> reply.</summary>
public sealed record ReflectIsDefaultReply(bool IsDefault);

/// <summary>
/// The one, always-injectable engine API. It owns the JSON-RPC connection to the engine's RpcCommunication
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
                (string level, string message) => LogReceived?.Invoke(new LogEntry(level, message)));
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
        var result = await InvokeAsync<ReflectIsDefaultReply>(
            "reflect.isDefault",
            new { EntityId = entityId, Component = component, Property = property },
            ct).ContinueOnAnyContext();
        return result is { Success: true, Value: { } reply }
            ? Result<bool>.Ok(reply.IsDefault)
            : Result<bool>.Fail(result.Error ?? "The engine returned no result.");
    }

    /// <summary>Describes a reflected type's properties and icon without needing a live instance.</summary>
    public Task<Result<ReflectedType>> DescribeTypeAsync(string typeName, CancellationToken ct) =>
        InvokeAsync<ReflectedType>("reflect.describeType", new { TypeName = typeName }, ct);

    /// <summary>Asks the engine to start streaming frames into shared memory, returning where and how.</summary>
    public Task<Result<ViewInfo>> StartViewAsync(CancellationToken ct) =>
        InvokeAsync<ViewInfo>("view.start", null, ct);

    /// <summary>Stops the engine's frame streaming.</summary>
    public Task<Result> StopViewAsync(CancellationToken ct) =>
        InvokeAsync("view.stop", null, ct);

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
