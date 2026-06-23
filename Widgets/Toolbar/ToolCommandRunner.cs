using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Rpc;
using Toybox.Studio.Services.World;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Widgets.Toolbar;

/// <summary>
/// Runs a data-driven toolbar command (an ordered list of RPC steps) against the engine. The generic RPC
/// step dispatch lives on <see cref="EngineRpc.RunAsync"/>; this runner adds the toolbar-specific glue —
/// stepping the command, routing <c>view.setGizmo</c> through <see cref="GizmoTool"/> and the
/// <c>editor.play</c>/<c>editor.stop</c>/<c>editor.togglePause</c> transport steps through <see cref="Session"/>,
/// and logging failures. Steps run in sequence and the command stops at the first failed awaited step.
/// </summary>
public sealed class ToolCommandRunner
{
    private readonly EngineRpc _engine;
    private readonly GizmoTool _gizmo;
    private readonly Session _session;
    private readonly Logger _log;

    public ToolCommandRunner(EngineRpc engine, GizmoTool gizmo, Session session, Logger log)
    {
        _engine = engine;
        _gizmo = gizmo;
        _session = session;
        _log = log;
    }

    /// <summary>Runs every step of <paramref name="command"/> in order, stopping at the first failure.</summary>
    public async Task<Result> RunAsync(ToolCommand command, CancellationToken ct)
    {
        foreach (var step in command.Steps)
        {
            var result = await RunStepAsync(step, ct).ContinueOnAnyContext();
            if (!result.Success)
                return result;
        }

        return Result.Ok();
    }

    private async Task<Result> RunStepAsync(ToolCommandStep step, CancellationToken ct)
    {
        switch (step.Kind)
        {
            case "rpc":
                return await RunRpcAsync(step.Rpc, ct).ContinueOnAnyContext();
            case "script":
                _log.Warning("Toolbar script steps are not supported yet; skipping.");
                return Result.Ok();
            default:
                _log.Warning($"Unknown toolbar step kind '{step.Kind}'; skipping.");
                return Result.Ok();
        }
    }

    private async Task<Result> RunRpcAsync(RpcCall? call, CancellationToken ct)
    {
        if (call is null || string.IsNullOrWhiteSpace(call.Method))
        {
            _log.Warning("Toolbar rpc step has no method; skipping.");
            return Result.Ok();
        }

        // The transform gizmo is editor-side state: it gates marquee-select and is pushed (and re-pushed on
        // reconnect) by GizmoSync. Route view.setGizmo through GizmoTool so a data-driven tool keeps both in
        // sync, instead of pushing the raw RPC behind the editor's back.
        if (call.Method == "view.setGizmo")
        {
            _gizmo.Mode = ParseGizmoMode(call.Params);
            return Result.Ok();
        }

        // Play / Stop / Pause are editor-coordinated transitions (snapshot/restore, pause clearing, the
        // loading phase), not raw engine calls — route them through the session, which owns that and its
        // own logging.
        switch (call.Method)
        {
            case "editor.play":
                await _session.StartPlayAsync().ContinueOnAnyContext();
                return Result.Ok();
            case "editor.stop":
                await _session.StopPlayAsync().ContinueOnAnyContext();
                return Result.Ok();
            case "editor.togglePause":
                await _session.SetPausedAsync(!_session.IsPaused).ContinueOnAnyContext();
                return Result.Ok();
        }

        var result = await _engine.RunAsync(call, ct).ContinueOnAnyContext();
        if (!result.Success)
            _log.Error($"Toolbar command step '{call.Method}' failed: {result.Error}");
        return result;
    }

    private static GizmoMode ParseGizmoMode(JObject? @params) => @params?["mode"]?.ToString() switch
    {
        "translate" => GizmoMode.Translate,
        "rotate" => GizmoMode.Rotate,
        "scale" => GizmoMode.Scale,
        _ => GizmoMode.None,
    };
}
