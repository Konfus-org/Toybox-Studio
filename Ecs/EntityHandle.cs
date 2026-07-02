namespace Toybox.Studio.Ecs;

/// <summary>
/// A lightweight, string-free reference to one entity in a <see cref="World"/>: its stable engine id plus,
/// when known, its display name. This is the entity-world counterpart of
/// <see cref="Toybox.Studio.Project.AssetHandle"/> — held where code wants to keep a reference to an
/// entity without carrying the whole node, then resolved to the full <see cref="Entity"/> on demand via
/// <see cref="World.Find(EntityHandle)"/>.
///
/// <see cref="None"/> is the empty handle (id 0) — the default for an unset entity field and what a failed
/// lookup returns.
/// </summary>
public readonly record struct EntityHandle(ulong Id, string Name = "")
{
    /// <summary>The empty handle (id 0): no entity.</summary>
    public static EntityHandle None => default;

    /// <summary>Whether this handle references no entity.</summary>
    public bool IsNone => Id == 0;

    /// <summary>A bare handle carrying only an id (no resolved name).</summary>
    public static EntityHandle FromId(ulong id) => new(id);

    public override string ToString() => IsNone ? "None" : string.IsNullOrEmpty(Name) ? $"#{Id}" : Name;
}
