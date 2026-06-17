using System.Collections.Generic;

namespace Toybox.Studio.Models.Ecs;

/// <summary>
/// A UI-ready snapshot of the engine's active world: its root entities, each carrying its own subtree
/// (scene + globals forests). Parsed from a world.describe reply and rebuilt on every refresh — the
/// persistent, observable layer the UI binds to is the Ecs view-models, which reconcile against this.
/// The engine owns the authoritative data; <see cref="Toybox.Studio.Services.World.WorldManager"/> publishes
/// a fresh snapshot whenever it changes.
/// </summary>
public sealed class World
{
    /// <summary>The empty world (no entities) — shown while disconnected or before the first snapshot.</summary>
    public static readonly World Empty = new([]);

    public World(IReadOnlyList<Entity> roots) => Roots = roots;

    /// <summary>The world's root entities.</summary>
    public IReadOnlyList<Entity> Roots { get; }
}
