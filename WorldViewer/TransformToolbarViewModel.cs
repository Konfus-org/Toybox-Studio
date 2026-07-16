using Avalonia.Media;
using Toybox.Studio.Events;
using Toybox.Studio.Keybindings;
using Toybox.Studio.Toolbar;

namespace Toybox.Studio.WorldViewer;

/// <summary>
/// The world viewport's transform toolbar: the gizmo mode radio row (Select / Move / Rotate / Scale)
/// plus the snapping toggle, docked top-centre by default. Checked states re-derive from the transform
/// tool on every <see cref="WorldViewerToolChanged"/>, so the row mirrors the tool no matter who
/// changed it (toolbar click, Q/W/E/R chord, or the Settings grid).
/// </summary>
public sealed class TransformToolbarViewModel : ToolbarViewModel, IEventHandler<WorldViewerToolChanged>
{
    // Each tool's glyph color, so the row reads at a glance: the pointer wears the editor's
    // lavender accent, the transform trio wear distinct hues, the magnet its own teal.
    private static readonly Color SelectColor = Color.FromRgb(0xA9, 0x9C, 0xF2);
    private static readonly Color TranslateColor = Color.FromRgb(0x5F, 0xD9, 0x80);
    private static readonly Color RotateColor = Color.FromRgb(0x5A, 0xA0, 0xF2);
    private static readonly Color ScaleColor = Color.FromRgb(0xF0, 0xA3, 0x4A);
    private static readonly Color SnapColor = Color.FromRgb(0x45, 0xC8, 0xB8);
    private static readonly Color OrientationColor = Color.FromRgb(0xC8, 0x8C, 0x45);

    private readonly WorldViewerTool _tool;

    private readonly ToolbarNumberField _snapField;

    public TransformToolbarViewModel(
        WorldViewerTool tool, ActionRegistry actions, EditorKeymap keymap, EventDispatcher events)
        : base("transform", ToolbarEdge.Top, CreateTools(tool, actions, keymap, events), events)
    {
        _tool = tool;
        // The snap-amount field edits the active mode's step. Its value bridges to the tool; its unit,
        // step and visibility all track the mode, so a WorldViewerToolChanged re-derives them.
        _snapField = new ToolbarNumberField(() => tool.SnapStep, tool.SetSnapStep);
        NumberField = _snapField;
        UpdateSnapField();
    }

    public void Handle(in WorldViewerToolChanged evt)
    {
        RefreshTools();
        UpdateSnapField();
    }

    // Re-derives the snap field from the tool: the mode's unit (a trailing ° rotating, a leading ×
    // scaling) and step, plus its value, and whether it shows at all — only while snapping is
    // effectively on (the magnet toggle, or the held snap key inverting it).
    private void UpdateSnapField()
    {
        _snapField.Prefix = _tool.Mode == GizmoMode.Scale ? "×" : string.Empty;
        _snapField.Suffix = _tool.Mode == GizmoMode.Rotate ? "°" : string.Empty;
        _snapField.Increment = _tool.Mode == GizmoMode.Rotate ? 1m : 0.1m;
        _snapField.IsVisible = _tool.EffectiveSnapping;
        _snapField.RefreshValue();
    }

    // The tool buttons in display order (the last is the snapping toggle).
    private static ToolbarItemViewModel[] CreateTools(
        WorldViewerTool tool, ActionRegistry actions, EditorKeymap keymap, EventDispatcher events) =>
    [
        new(ActionIds.GizmoSelect, SelectColor, () => tool.Mode == GizmoMode.Select, actions, keymap, events),
        new(ActionIds.GizmoTranslate, TranslateColor, () => tool.Mode == GizmoMode.Translate, actions, keymap, events),
        new(ActionIds.GizmoRotate, RotateColor, () => tool.Mode == GizmoMode.Rotate, actions, keymap, events),
        new(ActionIds.GizmoScale, ScaleColor, () => tool.Mode == GizmoMode.Scale, actions, keymap, events),
        new(ActionIds.GizmoToggleSnap, SnapColor, () => tool.SnappingEnabled, actions, keymap, events),
        new(ActionIds.GizmoToggleOrientation, OrientationColor,
            () => tool.Orientation == GizmoOrientation.Local, actions, keymap, events),
    ];
}
