namespace Toybox.Studio.Viewport;

/// <summary>
/// Handles a completed click-select tap on a viewport pane. The generic <see cref="ViewportViewModel"/>
/// owns tap detection and the (generic) pick RPC; it hands the resulting entity id here so the host
/// applies its own semantics — the world editor viewport lands it on the shared selection, honouring the
/// tap's toggle/additive modifiers. A viewport with no handler (game, asset preview) never picks.
/// </summary>
public interface IViewportPickHandler
{
    /// <summary>The tap resolved to <paramref name="entityId"/> (null on empty space); apply it.</summary>
    void OnPicked(ulong? entityId, ViewportTap tap);
}
