using Avalonia.Media;
using Toybox.Studio.Events;
using Toybox.Studio.Keybindings;
using Toybox.Studio.Toolbar;

namespace Toybox.Studio.WorldViewer;

/// <summary>
/// The world viewport's render-layers toolbar, docked top-right by default: the collider wireframe
/// toggles (all / on selection) and the post-processing toggle, then the render-stage radio row
/// (final / diffuse / normals / shadows / depth). Checked states re-derive from
/// <see cref="RenderLayers"/> on every <see cref="RenderLayersChanged"/>, so the row mirrors the
/// layers no matter who changed them.
/// </summary>
public sealed class RenderLayersToolbarViewModel : ToolbarViewModel, IEventHandler<RenderLayersChanged>
{
    // Glyph colors: the collider pair wear the wireframes' green, post its sparkle pink, and the
    // stage radio row a hue per output (normal-map lavender for normals, dusk blue for shadows...).
    private static readonly Color CollidersAllColor = Color.FromRgb(0x4C, 0xD9, 0x63);
    private static readonly Color CollidersSelectionColor = Color.FromRgb(0x9A, 0xE6, 0xA8);
    private static readonly Color PostProcessingColor = Color.FromRgb(0xF2, 0x8B, 0xD4);
    private static readonly Color FinalColor = Color.FromRgb(0xEF, 0xE6, 0xD4);
    private static readonly Color DiffuseColor = Color.FromRgb(0xF0, 0xA3, 0x4A);
    private static readonly Color NormalsColor = Color.FromRgb(0x8F, 0x8F, 0xFF);
    private static readonly Color ShadowsColor = Color.FromRgb(0x8A, 0x93, 0xC4);
    private static readonly Color DepthColor = Color.FromRgb(0xB0, 0xB0, 0xB0);

    public RenderLayersToolbarViewModel(
        RenderLayers layers, ActionRegistry actions, EditorKeymap keymap, EventDispatcher events)
        : base("renderLayers", ToolbarEdge.TopRight, CreateTools(layers, actions, keymap, events), events)
    {
    }

    public void Handle(in RenderLayersChanged evt) => RefreshTools();

    // The toggles first, then the stage radio row (Final is the "nothing overridden" member).
    private static ToolbarItemViewModel[] CreateTools(
        RenderLayers layers, ActionRegistry actions, EditorKeymap keymap, EventDispatcher events) =>
    [
        new(ActionIds.RenderLayersCollidersAll, CollidersAllColor,
            () => layers.CollidersAll, actions, keymap, events),
        new(ActionIds.RenderLayersCollidersOnSelection, CollidersSelectionColor,
            () => layers.CollidersOnSelection, actions, keymap, events),
        new(ActionIds.RenderLayersPostProcessing, PostProcessingColor,
            () => layers.PostProcessingEnabled, actions, keymap, events),
        new(ActionIds.RenderLayersStageFinal, FinalColor,
            () => layers.Stage == RenderStage.Final, actions, keymap, events),
        new(ActionIds.RenderLayersStageDiffuse, DiffuseColor,
            () => layers.Stage == RenderStage.Diffuse, actions, keymap, events),
        new(ActionIds.RenderLayersStageNormals, NormalsColor,
            () => layers.Stage == RenderStage.Normals, actions, keymap, events),
        new(ActionIds.RenderLayersStageShadows, ShadowsColor,
            () => layers.Stage == RenderStage.Shadows, actions, keymap, events),
        new(ActionIds.RenderLayersStageDepth, DepthColor,
            () => layers.Stage == RenderStage.Depth, actions, keymap, events),
    ];
}
