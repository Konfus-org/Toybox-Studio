using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Hosting;
using Toybox.Studio.Keybindings;
using Toybox.Studio.Utils;

namespace Toybox.Studio.WorldViewer;

/// <summary>
/// The world viewport's render layers — the one source of truth for the viewport visualization toggles,
/// however they are driven (toolbar buttons, keybindings, menus): the collider wireframe modes
/// (all colliders / the selection's), the post-processing toggle, and the active render stage
/// (final / diffuse / normals / shadows / depth). It executes the renderLayers actions'
/// <see cref="EditorActionInvoked"/>, dispatching one <see cref="RenderLayersChanged"/> per real change.
/// Every change pushes the whole state to the engine (<c>editor.setRenderLayers</c>) — and again on
/// every (re)connect, the engine's render layers being process state. Registration of its actions lives
/// with the world viewport (<see cref="WorldViewerToolbarActions"/>); this is pure execution. One
/// instance, created and disposed by the composition root.
/// </summary>
public sealed class RenderLayers : EventSubscriber,
    IEventHandler<EditorActionInvoked>,
    IEventHandler<ConnectionChanged>
{
    private readonly Engine _engine;

    public RenderLayers(Engine engine, EventDispatcher events)
        : base(events)
    {
        _engine = engine;
    }

    /// <summary>Whether every collider/trigger wireframe in the world renders (not just the
    /// selection's).</summary>
    public bool CollidersAll { get; private set; }

    /// <summary>Whether the selected entities' collider/trigger wireframes render; on out of the
    /// box (the editor's long-standing behavior).</summary>
    public bool CollidersOnSelection { get; private set; } = true;

    /// <summary>Whether post-processing runs on editor viewports.</summary>
    public bool PostProcessingEnabled { get; private set; } = true;

    /// <summary>The render stage editor viewports output; <see cref="RenderStage.Final"/> is the
    /// normal shaded frame.</summary>
    public RenderStage Stage { get; private set; } = RenderStage.Final;

    /// <summary>Switches the active render stage, announcing and pushing on a real change.</summary>
    public void SetStage(RenderStage stage)
    {
        if (Stage == stage)
            return;

        Stage = stage;
        AnnounceAndPush();
    }

    public void ToggleCollidersAll()
    {
        CollidersAll = !CollidersAll;
        AnnounceAndPush();
    }

    public void ToggleCollidersOnSelection()
    {
        CollidersOnSelection = !CollidersOnSelection;
        AnnounceAndPush();
    }

    public void TogglePostProcessing()
    {
        PostProcessingEnabled = !PostProcessingEnabled;
        AnnounceAndPush();
    }

    public void Handle(in EditorActionInvoked evt)
    {
        switch (evt.ActionId)
        {
            case ActionIds.RenderLayersCollidersAll:
                ToggleCollidersAll();
                break;
            case ActionIds.RenderLayersCollidersOnSelection:
                ToggleCollidersOnSelection();
                break;
            case ActionIds.RenderLayersPostProcessing:
                TogglePostProcessing();
                break;
            case ActionIds.RenderLayersStageFinal:
                SetStage(RenderStage.Final);
                break;
            case ActionIds.RenderLayersStageDiffuse:
                SetStage(RenderStage.Diffuse);
                break;
            case ActionIds.RenderLayersStageNormals:
                SetStage(RenderStage.Normals);
                break;
            case ActionIds.RenderLayersStageShadows:
                SetStage(RenderStage.Shadows);
                break;
            case ActionIds.RenderLayersStageDepth:
                SetStage(RenderStage.Depth);
                break;
        }
    }

    public void Handle(in ConnectionChanged evt)
    {
        if (evt.State == ConnectionState.Connected)
            Push();
    }

    private void AnnounceAndPush()
    {
        Events.Dispatch(new RenderLayersChanged());
        Push();
    }

    // The whole render-layers state as one editor.setRenderLayers notification (the engine treats
    // missing keys as "keep current", so always send every key).
    private void Push()
    {
        if (!_engine.IsConnected)
            return;

        var payload = new JObject
        {
            ["collidersAll"] = CollidersAll,
            ["collidersSelected"] = CollidersOnSelection,
            ["postProcessing"] = PostProcessingEnabled,
            ["stage"] = ToWire(Stage),
        };
        _engine.SendNotificationAsync(EngineCommands.EditorSetRenderLayers, payload).FireAndForget();
    }

    private static string ToWire(RenderStage stage) => stage switch
    {
        RenderStage.Diffuse => "diffuse",
        RenderStage.Normals => "normals",
        RenderStage.Shadows => "shadows",
        RenderStage.Depth => "depth",
        _ => "final",
    };
}
