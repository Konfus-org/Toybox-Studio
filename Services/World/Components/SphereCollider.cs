using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>Typed view of the engine's <c>sphere_collider</c> component (a solid sphere).</summary>
public sealed record SphereCollider : IComponentType<SphereCollider>
{
    public static string Wire => "sphere_collider";

    public float Radius { get; init; } = 0.5f;

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>
    {
        ["radius"] = ComponentJson.FloatNode(Radius),
    });

    public static SphereCollider FromComponentJson(JObject raw) => new()
    {
        Radius = ComponentJson.ReadFloat(raw["radius"], 0.5f),
        Raw = raw,
    };
}
