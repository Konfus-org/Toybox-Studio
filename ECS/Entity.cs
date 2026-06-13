namespace Toybox.Studio.ECS;

/// <summary>
/// A UI-ready snapshot of one entity in the world hierarchy, parsed from a world.describe reply. Rebuilt on
/// every refresh — the persistent, observable layer the UI binds to is the Ecs view-models, which reconcile
/// against these snapshots by <see cref="Id"/>.
/// </summary>
public sealed class Entity
{
    public required ulong Id { get; init; }

    public required string Name { get; init; }

    public required string Tag { get; init; }

    public required IReadOnlyList<Component> Components { get; init; }

    public List<Entity> Children { get; } = [];
}
