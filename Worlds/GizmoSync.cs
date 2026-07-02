using System;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Worlds;

/// <summary>
/// Pushes the editor's active transform tool to the engine (<c>view.setGizmo</c>) whenever it changes, and
/// re-pushes on (re)connect so a freshly launched engine matches the toolbar. Resolved once at startup.
/// </summary>
public sealed class GizmoSync : IDisposable
{
    private readonly GizmoTool _tool;
    private readonly Engine _engine;
    private readonly Session _session;

    public GizmoSync(GizmoTool tool, Engine engine, Session session)
    {
        _tool = tool;
        _engine = engine;
        _session = session;
        _tool.Changed += Push;
        _session.StateChanged += OnStateChanged;
    }

    public void Dispose()
    {
        _tool.Changed -= Push;
        _session.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(ConnectionState state)
    {
        if (state == ConnectionState.Connected)
            Push();
    }

    private void Push()
    {
        if (_engine.IsConnected)
            _engine.SendNotification(EngineMethods.ViewSetGizmo, new { Mode = ToWire(_tool.Mode) })
                .FireAndForget();
    }

    private static string ToWire(GizmoMode mode) => mode switch
    {
        GizmoMode.Translate => "translate",
        GizmoMode.Rotate => "rotate",
        GizmoMode.Scale => "scale",
        _ => "none",
    };
}
