using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Project.Assets;

/// <summary>
/// The serialized form of one entity inside a <see cref="Chunk"/> or <see cref="Globals"/> asset — its identity
/// (<see cref="Id"/>/<see cref="Name"/>/<see cref="Tag"/>/<see cref="Layer"/>), its <see cref="Parent"/>, and its
/// <see cref="Components"/> keyed by engine wire name. A plain value type, so the reflective bare codec
/// (<see cref="EngineApi.EngineSyncValue"/>) round-trips it by its public properties — no <c>[EngineSync]</c>
/// of its own; the owning asset's <c>entities</c> field carries the list.
///
/// Components are kept as their raw per-wire describe bodies (<see cref="JToken"/>) rather than typed
/// <c>Component</c> instances: the concrete component type for a wire is resolved from the registry, which the
/// generic codec can't do, so deep-typing the bodies is layered on separately. This already gives every entity and
/// component a faithful data mirror that round-trips edit→save.
/// </summary>
public sealed class EntityData
{
    public ulong Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Tag { get; set; } = string.Empty;

    public string Layer { get; set; } = string.Empty;

    /// <summary>The id of this entity's parent (0 = a root).</summary>
    public ulong Parent { get; set; }

    /// <summary>The entity's components, keyed by engine wire name (<c>transform</c>, <c>sky</c>, …); each value is
    /// that component's raw describe body.</summary>
    public Dictionary<string, JToken> Components { get; set; } = new();
}
