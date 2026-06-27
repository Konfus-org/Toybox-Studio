using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>Typed view of the engine's <c>mesh_collider</c> component (sourced from the entity's model).</summary>
public sealed record MeshCollider : IComponentType<MeshCollider>
{
    public static string Wire => "mesh_collider";

    public bool IsConvex { get; init; } = true;

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>
    {
        ["is_convex"] = ComponentJson.BoolNode(IsConvex),
    });

    public static MeshCollider FromComponentJson(JObject raw) => new()
    {
        IsConvex = ComponentJson.ReadBool(raw["is_convex"], true),
        Raw = raw,
    };
}
