using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services;

/// <summary>
/// One component on a scene entity. <see cref="Raw"/> is the original typed component JObject (kept so
/// edits can mutate one field and re-send the whole component); <see cref="Properties"/> is its parsed
/// type-driven property tree.
/// </summary>
public sealed record WorldComponent(string Name, JObject Raw, IReadOnlyList<PropertyNode> Properties);

/// <summary>
/// UI-ready snapshot of one entity in the scene hierarchy.
/// </summary>
public sealed class WorldNode
{
    public required ulong Id { get; init; }

    public required string Name { get; init; }

    public required string Tag { get; init; }

    public required IReadOnlyList<WorldComponent> Components { get; init; }

    public List<WorldNode> Children { get; } = [];
}
