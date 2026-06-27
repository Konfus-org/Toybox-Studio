using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>Typed view of the engine's <c>capsule_collider</c> component.</summary>
public sealed record CapsuleCollider : IComponentType<CapsuleCollider>
{
    public static string Wire => "capsule_collider";

    public float Radius { get; init; } = 0.5f;

    public float HalfHeight { get; init; } = 0.5f;

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>
    {
        ["radius"] = ComponentJson.FloatNode(Radius),
        ["half_height"] = ComponentJson.FloatNode(HalfHeight),
    });

    public static CapsuleCollider FromComponentJson(JObject raw) => new()
    {
        Radius = ComponentJson.ReadFloat(raw["radius"], 0.5f),
        HalfHeight = ComponentJson.ReadFloat(raw["half_height"], 0.5f),
        Raw = raw,
    };
}
