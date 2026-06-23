namespace Toybox.Studio.Services.World;

/// <summary>
/// A UI-ready snapshot of the engine's active world: its root entities, each carrying its own subtree
/// (streamed + globals forests). Parsed from a world.describe reply and rebuilt on every refresh — the
/// persistent, observable layer the UI binds to is the Ecs view-models, which reconcile against this.
/// The engine owns the authoritative data; <see cref="WorldManager"/> publishes
/// a fresh snapshot whenever it changes (and is itself the editor-side "World" construct that mutates it).
/// </summary>
public sealed class WorldDescription
{
    /// <summary>The empty world (no entities) — shown while disconnected or before the first snapshot.</summary>
    public static readonly WorldDescription Empty = new([]);

    public WorldDescription(IReadOnlyList<EntityDescription> roots) => Roots = roots;

    /// <summary>The world's root entities.</summary>
    public IReadOnlyList<EntityDescription> Roots { get; }
}
