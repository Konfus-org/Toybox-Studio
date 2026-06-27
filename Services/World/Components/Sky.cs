using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>The projection an engine sky material is shown on. Mirrors the engine <c>SkyType</c>.</summary>
public enum SkyShape
{
    Box = 0,
    Sphere = 1,
}

/// <summary>
/// Typed view of the engine's <c>sky</c> component: the environment <see cref="Material"/> and the
/// <see cref="Shape"/> it projects onto.
/// </summary>
public sealed record Sky : IComponentType<Sky>
{
    public static string Wire => "sky";

    /// <summary>The sky material instance drawn as the environment background.</summary>
    public MaterialInstance Material { get; init; }

    /// <summary>The projection shape (box / sphere).</summary>
    public SkyShape Shape { get; init; } = SkyShape.Sphere;

    /// <summary>The component body this was read from, so untyped fields round-trip on write (null when fresh).</summary>
    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>
    {
        ["material"] = Material.ToNode(),
        ["type"] = ComponentJson.EnumNode(Shape),
    });

    public static Sky FromComponentJson(JObject raw) => new()
    {
        Material = MaterialInstance.FromField(raw["material"]),
        Shape = ComponentJson.ReadEnum(raw["type"], SkyShape.Sphere),
        Raw = raw,
    };
}
