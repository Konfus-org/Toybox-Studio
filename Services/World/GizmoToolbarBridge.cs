using System;

namespace Toybox.Studio.Services.World;

/// <summary>
/// Bridges the editor's <see cref="GizmoTool"/> — the source of truth for the active transform tool and for
/// marquee-select gating — to the generic <see cref="ToolbarState"/> the data-driven toolbar binds to, so the
/// gizmo radio buttons reflect their checked state. Resolved once at startup, like <c>GizmoSync</c>.
/// </summary>
public sealed class GizmoToolbarBridge : IDisposable
{
    private const string Group = "gizmo";

    private readonly GizmoTool _tool;
    private readonly ToolbarState _state;

    public GizmoToolbarBridge(GizmoTool tool, ToolbarState state)
    {
        _tool = tool;
        _state = state;
        _tool.Changed += Sync;
        Sync();
    }

    public void Dispose() => _tool.Changed -= Sync;

    private void Sync() => _state.SetActive(Group, "gizmo:" + ToWire(_tool.Mode));

    private static string ToWire(GizmoMode mode) => mode switch
    {
        GizmoMode.Translate => "translate",
        GizmoMode.Rotate => "rotate",
        GizmoMode.Scale => "scale",
        _ => "none",
    };
}
