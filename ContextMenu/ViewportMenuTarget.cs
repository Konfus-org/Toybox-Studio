namespace Toybox.Studio.ContextMenu;

/// <summary>
/// The <see cref="IWorldMenuTarget"/> for a viewport right-click that carries no view-model: the viewport
/// raycasts the entity under the cursor (<see cref="EntityId"/> set) or hits nothing (<see cref="EntityId"/>
/// null), and opens the one world menu either way. The tree and inspector pass their own view-models, which are
/// world targets themselves.
/// </summary>
public sealed record ViewportMenuTarget(ulong? EntityId) : IWorldMenuTarget
{
    public WorldMenuSurface Surface => WorldMenuSurface.Viewport;
}
