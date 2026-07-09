using Avalonia.Media;
using Toybox.Studio.Events;
using Toybox.Studio.Keybindings;
using Toybox.Studio.Utils.Toolbars;
using Toybox.Studio.Worlds;

namespace Toybox.Studio.Toolbar;

/// <summary>
/// The viewport's transform toolbar: the gizmo mode radio row (Select / Move / Rotate / Scale) plus
/// the snapping toggle, docked top-centre by default. Checked states re-derive from the transform
/// tool on every <see cref="GizmoToolChanged"/>, so the row mirrors the tool no matter who changed
/// it (toolbar click, Q/W/E/R chord, or the Settings grid).
/// </summary>
public sealed class TransformToolbarViewModel : ToolbarViewModel, IEventHandler<GizmoToolChanged>
{
    // Each tool's glyph color, so the row reads at a glance: the pointer wears the editor's
    // lavender accent, the transform trio wear distinct hues, the magnet its own teal.
    private static readonly Color SelectColor = Color.FromRgb(0xA9, 0x9C, 0xF2);
    private static readonly Color TranslateColor = Color.FromRgb(0x5F, 0xD9, 0x80);
    private static readonly Color RotateColor = Color.FromRgb(0x5A, 0xA0, 0xF2);
    private static readonly Color ScaleColor = Color.FromRgb(0xF0, 0xA3, 0x4A);
    private static readonly Color SnapColor = Color.FromRgb(0x45, 0xC8, 0xB8);

    private readonly GizmoTool _tool;

    public TransformToolbarViewModel(
        GizmoTool tool, ActionRegistry actions, EditorKeymap keymap, EventDispatcher events)
        : base("transform", ToolbarEdge.Top, CreateTools(tool, actions, keymap, events), events)
    {
        _tool = tool;
        UpdateSnapAmount();
    }

    public void Handle(in GizmoToolChanged evt)
    {
        RefreshTools();
        UpdateSnapAmount();
    }

    // The snap-amount chip shows the active mode's step while snapping is effectively on (the magnet
    // toggle, or the held snap key inverting it); hidden otherwise.
    private void UpdateSnapAmount() =>
        Annotation = _tool.EffectiveSnapping ? _tool.SnapStepLabel : null;

    // The tool buttons in display order (the last is the snapping toggle).
    private static ToolbarItemViewModel[] CreateTools(
        GizmoTool tool, ActionRegistry actions, EditorKeymap keymap, EventDispatcher events) =>
    [
        new(ActionIds.GizmoSelect, SelectColor, () => tool.Mode == GizmoMode.Select, actions, keymap, events),
        new(ActionIds.GizmoTranslate, TranslateColor, () => tool.Mode == GizmoMode.Translate, actions, keymap, events),
        new(ActionIds.GizmoRotate, RotateColor, () => tool.Mode == GizmoMode.Rotate, actions, keymap, events),
        new(ActionIds.GizmoScale, ScaleColor, () => tool.Mode == GizmoMode.Scale, actions, keymap, events),
        new(ActionIds.GizmoToggleSnap, SnapColor, () => tool.SnappingEnabled, actions, keymap, events),
    ];
}
