using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Commands;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Rpc;
using Toybox.Studio.Services.World;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Widgets.Toolbar;

/// <summary>
/// Runs a data-driven toolbar or context-menu command (an ordered list of RPC steps) against the engine. The
/// generic RPC step dispatch lives on <see cref="EngineRpc.RunAsync"/>; this runner adds the editor glue —
/// stepping the command, routing <c>view.setGizmo</c> through <see cref="GizmoTool"/>, the
/// <c>editor.play</c>/<c>editor.stop</c>/<c>editor.togglePause</c> transport steps through <see cref="Session"/>,
/// and every other <c>editor.*</c> verb through <see cref="EditorCommands"/> (passing the
/// <see cref="MenuContext"/> a context menu was opened with) — and logging failures. Steps run in sequence and
/// the command stops at the first failed awaited step.
/// </summary>
public sealed class ToolCommandRunner
{
    private readonly EngineRpc _engine;
    private readonly GizmoTool _gizmo;
    private readonly Session _session;
    private readonly EditorCommands _editor;
    private readonly Logger _log;

    public ToolCommandRunner(
        EngineRpc engine, GizmoTool gizmo, Session session, EditorCommands editor, Logger log)
    {
        _engine = engine;
        _gizmo = gizmo;
        _session = session;
        _editor = editor;
        _log = log;
    }

    /// <summary>
    /// Runs every step of <paramref name="command"/> in order, stopping at the first failure. The optional
    /// <paramref name="context"/> tells the <c>editor.*</c> verbs what the menu was opened over (null for
    /// toolbar buttons, which act on the global selection alone).
    /// </summary>
    public async Task<Result> RunAsync(ToolCommand command, CancellationToken ct, MenuContext? context = null)
    {
        foreach (var step in command.Steps)
        {
            var result = await RunStepAsync(step, context, ct).ContinueOnAnyContext();
            if (!result.Success)
                return result;
        }

        return Result.Ok();
    }

    private async Task<Result> RunStepAsync(ToolCommandStep step, MenuContext? context, CancellationToken ct)
    {
        switch (step.Kind)
        {
            case "rpc":
                return await RunRpcAsync(step.Rpc, context, ct).ContinueOnAnyContext();
            case "script":
                _log.Warning("Toolbar script steps are not supported yet; skipping.");
                return Result.Ok();
            default:
                _log.Warning($"Unknown toolbar step kind '{step.Kind}'; skipping.");
                return Result.Ok();
        }
    }

    private async Task<Result> RunRpcAsync(RpcCall? call, MenuContext? context, CancellationToken ct)
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
        // Session's transport methods return a bare Task and log their own failure, so the only way to know
        // whether the transition actually happened is to inspect the session state afterwards. Surface a
        // failed transition as Result.Fail so the command stops instead of running later steps blindly.
        switch (call.Method)
        {
            case "editor.play":
                await _session.StartPlayAsync().ContinueOnAnyContext();
                return _session.IsPlaying
                    ? Result.Ok()
                    : Result.Fail("Entering play mode failed.");
            case "editor.stop":
                await _session.StopPlayAsync().ContinueOnAnyContext();
                return _session.IsPlaying
                    ? Result.Fail("Exiting play mode failed.")
                    : Result.Ok();
            case "editor.togglePause":
                var requestedPause = !_session.IsPaused;
                await _session.SetPausedAsync(requestedPause).ContinueOnAnyContext();
                return _session.IsPaused == requestedPause
                    ? Result.Ok()
                    : Result.Fail("Toggling pause failed.");
        }

        // Every other editor.* verb (delete/move/duplicate/clipboard/component ops) is editor-side state that
        // coordinates the selection, clipboard and the world's dirty/refresh cycle — route it through
        // EditorCommands with the menu's context rather than treating it as a raw engine call.
        if (call.Method.StartsWith("editor.", System.StringComparison.Ordinal))
            return await _editor.RunAsync(call.Method, call.Params, context, ct).ContinueOnAnyContext();

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
