using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>Typed view of the engine's <c>transform</c> component: local-space position, rotation and scale.</summary>
public sealed record Transform : IComponentType<Transform>
{
    public static string Wire => "transform";

    public Vector3 Position { get; init; } = Vector3.Zero;

    public Quaternion Rotation { get; init; } = Quaternion.Identity;

    public Vector3 Scale { get; init; } = Vector3.One;

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>
    {
        ["position"] = ComponentJson.Vec3Node(Position),
        ["rotation"] = ComponentJson.QuatNode(Rotation),
        ["scale"] = ComponentJson.Vec3Node(Scale),
    });

    public static Transform FromComponentJson(JObject raw) => new()
    {
        Position = ComponentJson.ReadVector3(raw["position"]),
        Rotation = ComponentJson.ReadQuaternion(raw["rotation"]),
        Scale = ComponentJson.ReadVector3(raw["scale"]),
        Raw = raw,
    };
}
