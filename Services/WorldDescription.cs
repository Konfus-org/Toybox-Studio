using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services;

/// <summary>
/// Wire shape of the engine's world.describe reply.
/// </summary>
public sealed record WorldDescription(IReadOnlyList<WorldEntity> Entities);

/// <summary>
/// One serialized entity as produced by the engine's reflection system.
/// </summary>
public sealed record WorldEntity(
    ulong Id,
    string? Name,
    string? Tag,
    string? Layer,
    ulong Parent,
    JObject? Components);
