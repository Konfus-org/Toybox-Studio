using Toybox.Studio.EngineApi.Types.Worlds;

namespace Toybox.Studio.WorldTree;

/// <summary>
/// The editor's current editing world — the one live <see cref="World"/> mirror the World Viewer hosts,
/// published here so world-domain features (the <see cref="WorldTreeViewModel"/> hierarchy, the entity
/// keybindings, the world context menu) can reach the entity/component handles without depending on the
/// viewport panel that owns it. The World Viewer sets it when it loads a world and clears it on teardown;
/// consumers read <see cref="Active"/> (null until a world loads) and observe <see cref="ActiveChanged"/>.
/// One instance, a composition-root singleton.
/// </summary>
public sealed class GameState
{
    /// <summary>The live editing world, or null before one has loaded.</summary>
    public World? Active { get; private set; }

    /// <summary>Raised whenever <see cref="Active"/> is replaced — a load, a reconnect rebuild, or a
    /// teardown. Fired on the caller's thread (the World Viewer publishes from the UI thread).</summary>
    public event Action? ActiveChanged;

    /// <summary>Publishes the world the World Viewer just hosted (or null on teardown) as the active
    /// editing world; a no-op when it is already the active one.</summary>
    public void SetActive(World? world)
    {
        if (ReferenceEquals(Active, world))
            return;

        Active = world;
        ActiveChanged?.Invoke();
    }
}
