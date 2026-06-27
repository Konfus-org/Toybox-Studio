using System.Collections.Generic;
using Avalonia.Media;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>Typed view of the engine's <c>spot_light</c> component.</summary>
public sealed record SpotLight : IComponentType<SpotLight>
{
    public static string Wire => "spot_light";

    public Color Color { get; init; } = Colors.White;

    public float Intensity { get; init; } = 1.0f;

    public bool CastShadows { get; init; } = true;

    public float Range { get; init; } = 10.0f;

    public float InnerAngle { get; init; } = 20.0f;

    public float OuterAngle { get; init; } = 35.0f;

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>
    {
        ["color"] = ComponentJson.ColorNode(Color),
        ["intensity"] = ComponentJson.FloatNode(Intensity),
        ["cast_shadows"] = ComponentJson.BoolNode(CastShadows),
        ["range"] = ComponentJson.FloatNode(Range),
        ["inner_angle"] = ComponentJson.FloatNode(InnerAngle),
        ["outer_angle"] = ComponentJson.FloatNode(OuterAngle),
    });

    public static SpotLight FromComponentJson(JObject raw) => new()
    {
        Color = ComponentJson.ReadColor(raw["color"]),
        Intensity = ComponentJson.ReadFloat(raw["intensity"], 1.0f),
        CastShadows = ComponentJson.ReadBool(raw["cast_shadows"], true),
        Range = ComponentJson.ReadFloat(raw["range"], 10.0f),
        InnerAngle = ComponentJson.ReadFloat(raw["inner_angle"], 20.0f),
        OuterAngle = ComponentJson.ReadFloat(raw["outer_angle"], 35.0f),
        Raw = raw,
    };
}
