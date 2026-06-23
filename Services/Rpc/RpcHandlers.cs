using StreamJsonRpc;

namespace Toybox.Studio.Services.Rpc;

/// <summary>
/// The handle a caller uses, during <see cref="RpcClient.ConnectAsync"/>, to register its inbound handlers
/// before the connection starts listening. Wraps <see cref="JsonRpc"/> so callers don't take a direct
/// dependency on the underlying transport library.
/// </summary>
public sealed class RpcHandlers
{
    private readonly JsonRpc _rpc;

    internal RpcHandlers(JsonRpc rpc) => _rpc = rpc;

    /// <summary>
    /// Registers <paramref name="handler"/> as the local handler for inbound <paramref name="method"/> calls;
    /// the delegate's parameters are deserialized from the call's arguments.
    /// </summary>
    public void On(string method, Delegate handler) => _rpc.AddLocalRpcMethod(method, handler);
}
