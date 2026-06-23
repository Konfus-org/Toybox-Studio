using Toybox.Studio.Services.Rpc;

namespace Toybox.Studio.Widgets.Toolbar;

/// <summary>
/// One step of a tool command. A flat, <see cref="Kind"/>-discriminated shape (not a type hierarchy, not
/// Newtonsoft <c>TypeNameHandling</c>) so new step kinds add fields while staying forward/back compatible on
/// disk. Today only <c>"rpc"</c> is executed; <c>"script"</c> is reserved and unknown kinds are skipped.
/// </summary>
public sealed class ToolCommandStep
{
    /// <summary>The step kind: <c>"rpc"</c> (run <see cref="Rpc"/>) today; <c>"script"</c> reserved.</summary>
    public string Kind { get; set; } = "rpc";

    /// <summary>The RPC call to make when <see cref="Kind"/> is <c>"rpc"</c>.</summary>
    public RpcCall? Rpc { get; set; }

    /// <summary>Reserved for a future <c>"script"</c> kind; null today.</summary>
    public string? Script { get; set; }
}
