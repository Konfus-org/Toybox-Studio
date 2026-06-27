using System.Collections.Generic;
using Avalonia.Media;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>Typed view of the engine's <c>point_light</c> component.</summary>
public sealed record PointLight : IComponentType<PointLight>
{
    public static string Wire => "point_light";

    public Color Color { get; init; } = Colors.White;

    public float Intensity { get; init; } = 1.0f;

    public bool CastShadows { get; init; } = true;

    public float Range { get; init; } = 10.0f;

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>
    {
        ["color"] = ComponentJson.ColorNode(Color),
        ["intensity"] = ComponentJson.FloatNode(Intensity),
        ["cast_shadows"] = ComponentJson.BoolNode(CastShadows),
        ["range"] = ComponentJson.FloatNode(Range),
    });

    public static PointLight FromComponentJson(JObject raw) => new()
    {
        Color = ComponentJson.ReadColor(raw["color"]),
        Intensity = ComponentJson.ReadFloat(raw["intensity"], 1.0f),
        CastShadows = ComponentJson.ReadBool(raw["cast_shadows"], true),
        Range = ComponentJson.ReadFloat(raw["range"], 10.0f),
        Raw = raw,
    };
}
