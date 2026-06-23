using Toybox.Studio.Utils;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json.Serialization;
using StreamJsonRpc;

namespace Toybox.Studio.Services.Rpc;

/// <summary>
/// A generic JSON-RPC client over a newline-delimited JSON stream on loopback TCP. It owns the socket and the
/// <see cref="JsonRpc"/> connection, retries the connect while the peer is still booting, exposes the
/// request / notification primitives, and raises <see cref="Disconnected"/> when the link drops. It knows
/// nothing about any particular peer's methods — a caller registers its inbound handlers at connect time
/// (via the <see cref="RpcHandlers"/> passed to <see cref="ConnectAsync"/>) and layers its own domain calls on
/// top of <see cref="InvokeAsync{T}"/> / <see cref="NotifyAsync"/>. Every call returns a <see cref="Result"/>
/// with a helpful message on failure (including "not connected") rather than throwing.
/// </summary>
public sealed class RpcClient : IAsyncDisposable
{
    private const string NotConnected = "Not connected.";

    private TcpClient? _tcpClient;
    private JsonRpc? _rpc;

    /// <summary>Raised when the connection drops for any reason.</summary>
    public event Action? Disconnected;

    public bool IsConnected => _rpc is { IsDisposed: false };

    /// <summary>
    /// Connects to <paramref name="port"/> on loopback (retrying until the peer accepts the connection or
    /// <paramref name="timeout"/> elapses), lets <paramref name="registerHandlers"/> wire up the inbound
    /// notification handlers, then starts listening. Returns a failure <see cref="Result"/> (rather than
    /// throwing) on timeout or any connection error. Any handshake is the caller's job, made after this returns.
    /// </summary>
    public async Task<Result> ConnectAsync(
        int port, TimeSpan timeout, Action<RpcHandlers> registerHandlers, CancellationToken ct)
    {
        try
        {
            _tcpClient = await ConnectWithRetryAsync(port, timeout, ct).ContinueOnAnyContext();

            var formatter = new JsonMessageFormatter();
            formatter.JsonSerializer.ContractResolver = new CamelCasePropertyNamesContractResolver();

            var stream = _tcpClient.GetStream();
            var rpc = new JsonRpc(new NewLineDelimitedMessageHandler(stream, stream, formatter));
            // Inbound handlers must be registered before listening starts, so the caller wires them here.
            registerHandlers(new RpcHandlers(rpc));
            rpc.Disconnected += (_, _) => Disconnected?.Invoke();
            rpc.StartListening();
            _rpc = rpc;
            return Result.Ok();
        }
        catch (Exception exception)
        {
            Disconnect();
            return Result.Fail(exception.Message);
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

    /// <summary>
    /// Invokes a remote method expecting a typed reply. Returns a failure <see cref="Result"/> (rather than
    /// throwing) on any error, including "not connected".
    /// </summary>
    public async Task<Result<T>> InvokeAsync<T>(string method, object? args, CancellationToken ct)
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

    /// <summary>Invokes a remote method whose reply is ignored (success/failure only).</summary>
    public async Task<Result> InvokeAsync(string method, object? args, CancellationToken ct)
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

    /// <summary>Sends a fire-and-forget notification (no reply); a no-op when not connected.</summary>
    public Task NotifyAsync(string method, object? args)
    {
        var rpc = _rpc;
        if (rpc is null || rpc.IsDisposed)
            return Task.CompletedTask;

        return args is null ? rpc.NotifyAsync(method) : rpc.NotifyWithParameterObjectAsync(method, args);
    }

    /// <summary>
    /// Executes a data-driven <see cref="RpcCall"/>: a fire-and-forget notification when
    /// <see cref="RpcCall.Notify"/> is set, otherwise an awaited request whose failure is returned.
    /// </summary>
    public async Task<Result> RunAsync(RpcCall call, CancellationToken ct)
    {
        if (call.Notify)
        {
            await NotifyAsync(call.Method, call.Params).ContinueOnAnyContext();
            return Result.Ok();
        }

        return await InvokeAsync(call.Method, call.Params, ct).ContinueOnAnyContext();
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
                    throw new TimeoutException($"The RPC peer did not accept a connection on port {port} within {timeout.TotalSeconds:F0}s.");

                await Task.Delay(TimeSpan.FromMilliseconds(250), ct).ContinueOnAnyContext();
            }
        }
    }
}
