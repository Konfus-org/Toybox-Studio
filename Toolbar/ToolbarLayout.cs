using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Rpc;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Toolbar;

/// <summary>
/// The per-viewport toolbar configuration: which edge it is docked against and its ordered tools (the on-disk
/// order is the on-screen order). Persisted inside the dock layout (carried by the viewport's dock tool), so
/// each viewport remembers its own toolbar across restarts.
/// </summary>
public sealed class ToolbarLayout
{
    public ToolbarEdge DockedEdge { get; set; } = ToolbarEdge.Top;

    public List<ToolbarItem> Tools { get; set; } = [];

    /// <summary>
    /// The built-in default tool set: the transform gizmo radio group (Select / Move / Rotate / Scale),
    /// matching the legacy hardcoded toolbar. Seeded onto a fresh viewport.
    /// </summary>
    public static ToolbarLayout Default() => new()
    {
        DockedEdge = ToolbarEdge.Top,
        Tools =
        [
            Gizmo("builtin.gizmo.select", Icon.MousePointer2, "Select (box-select)", "none"),
            Gizmo("builtin.gizmo.move", Icon.Move3d, "Move", "translate"),
            Gizmo("builtin.gizmo.rotate", Icon.Rotate3d, "Rotate", "rotate"),
            Gizmo("builtin.gizmo.scale", Icon.Scale3d, "Scale", "scale"),
        ],
    };

    /// <summary>
    /// The game view's transport tool set: a Play button (shown only while stopped), then Stop and a
    /// Pause/Resume toggle (shown only while playing). The play/stop/pause steps are editor-coordinated
    /// actions routed through <c>ToolCommandRunner</c> to the session, not raw engine RPCs.
    /// </summary>
    public static ToolbarLayout GameTransport() => new()
    {
        DockedEdge = ToolbarEdge.Top,
        Tools =
        [
            Transport("builtin.play", Icon.Play, PaletteColor.Green, "Play", GameModeCondition.Off, "editor.play"),
            Transport("builtin.stop", Icon.Square, PaletteColor.Red, "Stop", GameModeCondition.On, "editor.stop"),
            Pause("builtin.pause", Icon.Pause, "Pause / Resume", "editor.togglePause"),
        ],
    };

    // A play/stop action tool: a one-step command carrying its semantic icon colour, shown for the given
    // play-mode condition.
    private static ToolbarItem Transport(
        string id, Icon icon, PaletteColor color, string tooltip, GameModeCondition mode, string method) => new()
    {
        Id = id,
        Icon = icon,
        IconColor = color,
        Tooltip = tooltip,
        GameMode = mode,
        Command = Single(method),
    };

    // Pause/Resume: a toggle in the "transport" group, checked while paused (PlayToolbarBridge mirrors the
    // session's pause state into that group), shown only while playing.
    private static ToolbarItem Pause(string id, Icon icon, string tooltip, string method) => new()
    {
        Id = id,
        Icon = icon,
        Tooltip = tooltip,
        GameMode = GameModeCondition.On,
        Group = "transport",
        ActiveStateKey = "transport:paused",
        Command = Single(method),
    };

    // A one-step command invoking a single method (its params unused).
    private static ToolCommand Single(string method) => new()
    {
        Steps = [new ToolCommandStep { Kind = "rpc", Rpc = new RpcCall { Method = method } }],
    };

    // A gizmo tool is a one-step rpc command notifying view.setGizmo, in the "gizmo" radio group, active
    // when ToolbarState's "gizmo" group equals "gizmo:<mode>".
    private static ToolbarItem Gizmo(string id, Icon icon, string tooltip, string mode) => new()
    {
        Id = id,
        Icon = icon,
        Tooltip = tooltip,
        Group = "gizmo",
        ActiveStateKey = "gizmo:" + mode,
        Command = new ToolCommand
        {
            Steps =
            [
                new ToolCommandStep
                {
                    Kind = "rpc",
                    Rpc = new RpcCall
                    {
                        Method = "view.setGizmo",
                        Notify = true,
                        Params = new JObject { ["mode"] = mode },
                    },
                },
            ],
        },
    };
}
