using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Keybindings;
using Toybox.Studio.Viewport;
using Icon = IconPacks.Avalonia.Lucide.PackIconLucideKind;

namespace Toybox.Studio.WorldViewer;

/// <summary>
/// Registers the world viewport's editor actions — the transform gizmo tools (Q/W/E/R + snap) and the
/// render-layer toggles — scoped to this panel via <see cref="Scheme.For{TViewModel}"/> over
/// <see cref="WorldViewerViewModel"/>, so they fire only while the world viewport has focus. The tools
/// that execute them (<see cref="WorldViewerTool"/>, <see cref="RenderLayers"/>) are pure handlers;
/// this is the single place their bindings and default chords are declared — the world viewport owns its
/// own keybindings. It also pulls in the generic viewport bundle under the same scope
/// (<see cref="ViewportActions"/>). Constructed once by the composition root before the keymap builds.
/// </summary>
public sealed class WorldViewerToolbarActions
{
    public WorldViewerToolbarActions(ActionRegistry actions)
    {
        var scheme = Scheme.For<WorldViewerViewModel>();
        RegisterTransformTools(actions, scheme);
        RegisterRenderLayers(actions, scheme);
        ViewportActions.Register(actions, scheme);
    }

    private static void RegisterTransformTools(ActionRegistry actions, string scheme)
    {
        actions.Register(new EditorAction
        {
            Id = ActionIds.GizmoSelect,
            Title = "Select Tool",
            Category = "Viewport",
            Icon = Icon.MousePointer2,
            Scheme = scheme,
            DefaultChords = [Chord(InputKey.Q)],
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.GizmoTranslate,
            Title = "Move Tool",
            Category = "Viewport",
            Icon = Icon.Move3d,
            Scheme = scheme,
            DefaultChords = [Chord(InputKey.W)],
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.GizmoRotate,
            Title = "Rotate Tool",
            Category = "Viewport",
            Icon = Icon.Rotate3d,
            Scheme = scheme,
            DefaultChords = [Chord(InputKey.E)],
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.GizmoScale,
            Title = "Scale Tool",
            Category = "Viewport",
            Icon = Icon.Scale3d,
            Scheme = scheme,
            DefaultChords = [Chord(InputKey.R)],
        });
        // A held modifier, not a pressed chord: the dispatcher never matches it (a bare modifier
        // down isn't a chord); its binding exists so the keybindings page can rebind which held key
        // snaps — the engine reads it live from the forwarded input.
        actions.Register(new EditorAction
        {
            Id = ActionIds.GizmoSnapHold,
            Title = "Snap (Hold)",
            Category = "Viewport",
            Icon = Icon.Magnet,
            Scheme = scheme,
            DefaultChords = [Chord(InputKey.LCtrl)],
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.GizmoToggleSnap,
            Title = "Toggle Snapping",
            Category = "Viewport",
            Icon = Icon.Magnet,
            Scheme = scheme,
        });
        // Global (world axes) vs local (the primary entity's axes). Godot binds this to T.
        actions.Register(new EditorAction
        {
            Id = ActionIds.GizmoToggleOrientation,
            Title = "Toggle Local/Global",
            Category = "Viewport",
            Icon = Icon.Axis3d,
            Scheme = scheme,
            DefaultChords = [Chord(InputKey.T)],
        });
    }

    private static void RegisterRenderLayers(ActionRegistry actions, string scheme)
    {
        actions.Register(new EditorAction
        {
            Id = ActionIds.RenderLayersCollidersAll,
            Title = "Show All Colliders",
            Category = "Viewport",
            Icon = Icon.Boxes,
            Scheme = scheme,
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.RenderLayersCollidersOnSelection,
            Title = "Show Colliders On Selection",
            Category = "Viewport",
            Icon = Icon.SquareDashedMousePointer,
            Scheme = scheme,
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.RenderLayersPostProcessing,
            Title = "Post-Processing",
            Category = "Viewport",
            Icon = Icon.Sparkles,
            Scheme = scheme,
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.RenderLayersStageFinal,
            Title = "Render Final Frame",
            Category = "Viewport",
            Icon = Icon.Eye,
            Scheme = scheme,
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.RenderLayersStageDiffuse,
            Title = "Render Diffuse Only",
            Category = "Viewport",
            Icon = Icon.Palette,
            Scheme = scheme,
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.RenderLayersStageNormals,
            Title = "Render Normals Only",
            Category = "Viewport",
            Icon = Icon.Compass,
            Scheme = scheme,
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.RenderLayersStageShadows,
            Title = "Render Shadows Only",
            Category = "Viewport",
            Icon = Icon.Eclipse,
            Scheme = scheme,
        });
        actions.Register(new EditorAction
        {
            Id = ActionIds.RenderLayersStageDepth,
            Title = "Render Depth Only",
            Category = "Viewport",
            Icon = Icon.Layers,
            Scheme = scheme,
        });
    }

    private static KeyChordInputControl Chord(InputKey key) => new() { Key = key };
}
