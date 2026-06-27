using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>Typed view of the engine's <c>box_collider</c> component (a solid axis-aligned box).</summary>
public sealed record BoxCollider : IComponentType<BoxCollider>
{
    public static string Wire => "box_collider";

    public Vector3 HalfExtents { get; init; } = new(0.5f, 0.5f, 0.5f);

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>
    {
        ["half_extents"] = ComponentJson.Vec3Node(HalfExtents),
    });

    public static BoxCollider FromComponentJson(JObject raw) => new()
    {
        HalfExtents = ComponentJson.ReadVector3(raw["half_extents"]),
        Raw = raw,
    };
}
