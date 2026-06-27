using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>
/// Typed view of the engine's <c>lods</c> component. <see cref="RenderDistance"/> is typed; the per-band LOD
/// list round-trips untouched through <see cref="Raw"/> (edit it via the dynamic component path).
/// </summary>
public sealed record Lods : IComponentType<Lods>
{
    public static string Wire => "lods";

    public float RenderDistance { get; init; }

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>
    {
        ["render_distance"] = ComponentJson.FloatNode(RenderDistance),
    });

    public static Lods FromComponentJson(JObject raw) => new()
    {
        RenderDistance = ComponentJson.ReadFloat(raw["render_distance"]),
        Raw = raw,
    };
}
