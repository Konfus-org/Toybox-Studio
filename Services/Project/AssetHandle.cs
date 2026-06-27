namespace Toybox.Studio.Services.Project;

/// <summary>
/// A lightweight, string-free reference to an engine asset: its stable id plus, when resolved through the
/// catalog, its display name / type / project-relative path. This is what typed component fields (e.g. a
/// Renderer's model) hold and what <see cref="AssetCatalog"/> vends, replacing raw <c>long</c> ids and path
/// strings at call sites.
///
/// <see cref="None"/> is the empty handle (id 0) — the default for an unset asset field and what a failed
/// lookup returns.
/// </summary>
public readonly record struct AssetHandle(long Id, string Name = "", string Type = "", string Path = "")
{
    /// <summary>The empty handle (id 0): no asset.</summary>
    public static AssetHandle None => default;

    /// <summary>Whether this handle references no asset.</summary>
    public bool IsNone => Id == 0;

    /// <summary>A bare handle carrying only an id (no resolved name/type/path).</summary>
    public static AssetHandle FromId(long id) => new(id);

    public override string ToString() => IsNone ? "None" : string.IsNullOrEmpty(Name) ? $"#{Id}" : Name;
}
