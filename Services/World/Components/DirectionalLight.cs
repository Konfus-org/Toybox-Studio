using System.Collections.Generic;
using Avalonia.Media;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>Typed view of the engine's <c>directional_light</c> component.</summary>
public sealed record DirectionalLight : IComponentType<DirectionalLight>
{
    public static string Wire => "directional_light";

    public Color Color { get; init; } = Colors.White;

    public float Intensity { get; init; } = 1.0f;

    public bool CastShadows { get; init; } = true;

    public float Ambient { get; init; } = 0.03f;

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>
    {
        ["color"] = ComponentJson.ColorNode(Color),
        ["intensity"] = ComponentJson.FloatNode(Intensity),
        ["cast_shadows"] = ComponentJson.BoolNode(CastShadows),
        ["ambient"] = ComponentJson.FloatNode(Ambient),
    });

    public static DirectionalLight FromComponentJson(JObject raw) => new()
    {
        Color = ComponentJson.ReadColor(raw["color"]),
        Intensity = ComponentJson.ReadFloat(raw["intensity"], 1.0f),
        CastShadows = ComponentJson.ReadBool(raw["cast_shadows"], true),
        Ambient = ComponentJson.ReadFloat(raw["ambient"], 0.03f),
        Raw = raw,
    };
}
