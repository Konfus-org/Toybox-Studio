using Toybox.Studio.EngineApi.Types.Worlds;
using Toybox.Studio.Viewport;

namespace Toybox.Studio.WorldViewer;

/// <summary>
/// The world editor viewport's click-select: lands a completed viewport tap on the shared
/// <see cref="WorldSelection"/> (whose synced push lights the engine's selection outline and anchors the
/// gizmo), honouring the tap's Ctrl-toggle / Shift-add modifiers and clearing on an empty click. The
/// generic viewport runs the pick RPC and hands the resolved entity id here (already on the UI thread).
/// </summary>
public sealed class WorldPickHandler(WorldSelection selection) : IViewportPickHandler
{
    public void OnPicked(ulong? entityId, ViewportTap tap)
    {
        if (entityId is { } value)
        {
            if (tap.Toggle)
                selection.Toggle(value);
            else if (tap.Additive)
                selection.Add(value);
            else
                selection.Set(value);
        }
        else if (!tap.Toggle && !tap.Additive)
        {
            selection.Clear();
        }
    }
}
