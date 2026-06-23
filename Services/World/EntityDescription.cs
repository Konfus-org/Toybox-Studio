namespace Toybox.Studio.Services.World;

/// <summary>
/// A UI-ready snapshot of one entity in the world hierarchy, parsed from a world.describe reply. Rebuilt on
/// every refresh — the persistent, observable layer the UI binds to is the Ecs view-models, which reconcile
/// against these snapshots by <see cref="Id"/>. The behavioral counterpart (rename/enable/move/… operations
/// against the engine) is the <see cref="Entity"/> handle.
/// </summary>
public sealed class EntityDescription
{
    public required ulong Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The entity's gameplay tags (UE-style, hierarchical dot-names). Only the engine's serialized
    /// tags appear here; transient runtime tags such as <c>editor.selected</c> are engine-internal.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Explicit sibling order from the engine; entities sharing a parent sort by this (then name).</summary>
    public int Order { get; init; }

    /// <summary>True when the engine marks this entity global (a full-lifetime resident). Drives whether it
    /// appears in the world view's Globals section rather than the world tree.</summary>
    public bool IsGlobal { get; init; }

    /// <summary>Whether the entity is enabled. A disabled entity is turned off wholesale (skipped by every
    /// runtime system) but still appears in the editor so it can be re-enabled. Defaults true.</summary>
    public bool IsEnabled { get; init; } = true;

    public required IReadOnlyList<ComponentDescription> Components { get; init; }

    public List<EntityDescription> Children { get; } = [];
}
