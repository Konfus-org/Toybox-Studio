using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace Toybox.Studio.Services;

/// <summary>
/// The engine's reply to the editor.hello handshake.
/// </summary>
public sealed record EngineHello(int ProtocolVersion, string Engine, string App);

/// <summary>
/// The engine's reply to view.start: where and how frames are shared.
/// </summary>
public sealed record ViewInfo(string Name, string Format);

/// <summary>
/// A single JSON-RPC connection to the engine's RpcCommunication plugin, speaking
/// newline-delimited JSON over loopback TCP.
/// </summary>
public sealed class EngineRpcClient : IAsyncDisposable
{
    private TcpClient? _tcpClient;
    private JsonRpc? _rpc;

    /// <summary>
    /// Raised for every engine.log notification streamed by the engine.
    /// </summary>
    public event Action<LogEntry>? LogReceived;

    /// <summary>
    /// Raised when the connection drops for any reason.
    /// </summary>
    public event Action? Disconnected;

    public bool IsConnected => _rpc is { IsDisposed: false };

    /// <summary>
    /// Connects, retrying while the engine boots, then performs the hello handshake.
    /// </summary>
    public async Task<EngineHello> ConnectAsync(int port, TimeSpan timeout, CancellationToken ct)
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

        return await _rpc
            .InvokeWithParameterObjectAsync<EngineHello>(
                "editor.hello",
                new { ProtocolVersion = 1, Client = "Toybox Studio" },
                ct).ContinueOnAnyContext();
    }

    /// <summary>
    /// Round-trips an engine.ping and returns the measured latency.
    /// </summary>
    public async Task<TimeSpan> PingAsync(CancellationToken ct)
    {
        var rpc = _rpc ?? throw new InvalidOperationException("Not connected.");
        var stopwatch = Stopwatch.StartNew();
        await rpc.InvokeWithCancellationAsync("engine.ping", null, ct).ContinueOnAnyContext();
        return stopwatch.Elapsed;
    }

    /// <summary>
    /// Pauses or resumes the engine's simulation; rendering keeps running.
    /// </summary>
    public Task SetPausedAsync(bool isPaused, CancellationToken ct)
    {
        var rpc = _rpc ?? throw new InvalidOperationException("Not connected.");
        return rpc.InvokeWithParameterObjectAsync("engine.setPaused", new { IsPaused = isPaused }, ct);
    }

    /// <summary>
    /// Pushes one editor-originated log line into the engine's unified log (file + console). Sent as a
    /// fire-and-forget notification; the engine suppresses the echo so the editor doesn't see it twice.
    /// </summary>
    public Task WriteEditorLogAsync(string level, string message)
    {
        var rpc = _rpc;
        if (rpc is null || rpc.IsDisposed)
            return Task.CompletedTask;

        return rpc.NotifyWithParameterObjectAsync("editor.log", new { Level = level, Message = message });
    }

    /// <summary>
    /// Pushes the editor's log colors (hex strings) to the engine so its own console output uses
    /// the same palette as the editor's console for each level.
    /// </summary>
    public Task SetLogColorsAsync(string info, string warning, string error, CancellationToken ct)
    {
        var rpc = _rpc ?? throw new InvalidOperationException("Not connected.");
        return rpc.InvokeWithParameterObjectAsync(
            "engine.setLogColors",
            new { Info = info, Warning = warning, Error = error },
            ct);
    }

    /// <summary>
    /// Asks the engine to exit gracefully.
    /// </summary>
    public Task RequestShutdownAsync(CancellationToken ct)
    {
        var rpc = _rpc ?? throw new InvalidOperationException("Not connected.");
        return rpc.InvokeWithCancellationAsync("engine.shutdown", null, ct);
    }

    /// <summary>
    /// Fetches the active world's entities with their reflected component data.
    /// </summary>
    public Task<WorldDescription> DescribeWorldAsync(CancellationToken ct)
    {
        var rpc = _rpc ?? throw new InvalidOperationException("Not connected.");
        return rpc.InvokeWithCancellationAsync<WorldDescription>("world.describe", null, ct);
    }

    /// <summary>
    /// Replaces one component on an entity with the given typed JSON. The engine tries to apply it and
    /// returns a JSON-RPC error (surfaced here as an exception) when the value cannot be applied.
    /// </summary>
    public Task SetComponentAsync(
        ulong entityId,
        string component,
        Newtonsoft.Json.Linq.JObject value,
        CancellationToken ct)
    {
        var rpc = _rpc ?? throw new InvalidOperationException("Not connected.");
        return rpc.InvokeWithParameterObjectAsync(
            "entity.setComponent",
            new { EntityId = entityId, Component = component, Value = value },
            ct);
    }

    /// <summary>
    /// Asks the engine to start streaming frames into shared memory.
    /// </summary>
    public Task<ViewInfo> StartViewAsync(CancellationToken ct)
    {
        var rpc = _rpc ?? throw new InvalidOperationException("Not connected.");
        return rpc.InvokeWithCancellationAsync<ViewInfo>("view.start", null, ct);
    }

    /// <summary>
    /// Stops the engine's frame streaming.
    /// </summary>
    public Task StopViewAsync(CancellationToken ct)
    {
        var rpc = _rpc ?? throw new InvalidOperationException("Not connected.");
        return rpc.InvokeWithCancellationAsync("view.stop", null, ct);
    }

    public ValueTask DisposeAsync()
    {
        _rpc?.Dispose();
        _rpc = null;
        _tcpClient?.Dispose();
        _tcpClient = null;
        return ValueTask.CompletedTask;
    }

    private static async Task<TcpClient> ConnectWithRetryAsync(
        int port,
        TimeSpan timeout,
        CancellationToken ct)
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
