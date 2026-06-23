using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.Rpc;

/// <summary>
/// One data-driven RPC call: a method name plus an optional params object passed verbatim to the peer.
/// <see cref="Notify"/> selects fire-and-forget (a JSON-RPC notification) versus an awaited request whose
/// failure is reported back. Executed by <see cref="RpcClient.RunAsync"/>; the building block of a
/// data-driven tool command (see <c>Widgets/ViewportToolbar/ToolCommand.cs</c>).
/// </summary>
public sealed class RpcCall
{
    /// <summary>The RPC method name (e.g. <c>view.setGizmo</c>, <c>myplugin.doThing</c>).</summary>
    public string Method { get; set; } = "";

    /// <summary>The JSON params object passed verbatim to the peer; null = no params.</summary>
    public JObject? Params { get; set; }

    /// <summary>
    /// When true the call is a fire-and-forget notification (no reply awaited), like <c>view.setGizmo</c>;
    /// when false it is awaited and a failure stops the rest of the command.
    /// </summary>
    public bool Notify { get; set; }
}
