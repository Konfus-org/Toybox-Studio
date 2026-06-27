using System.Numerics;
using Avalonia.Media;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>Typed view of the engine's <c>area_light</c> component.</summary>
public sealed record AreaLight : IComponentType<AreaLight>
{
    public static string Wire => "area_light";

    public Color Color { get; init; } = Colors.White;

    public float Intensity { get; init; } = 1.0f;

    public bool CastShadows { get; init; } = true;

    public float Range { get; init; } = 10.0f;

    public Vector2 AreaSize { get; init; } = Vector2.One;

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>
    {
        ["color"] = ComponentJson.ColorNode(Color),
        ["intensity"] = ComponentJson.FloatNode(Intensity),
        ["cast_shadows"] = ComponentJson.BoolNode(CastShadows),
        ["range"] = ComponentJson.FloatNode(Range),
        ["area_size"] = ComponentJson.Vec2Node(AreaSize),
    });

    public static AreaLight FromComponentJson(JObject raw) => new()
    {
        Color = ComponentJson.ReadColor(raw["color"]),
        Intensity = ComponentJson.ReadFloat(raw["intensity"], 1.0f),
        CastShadows = ComponentJson.ReadBool(raw["cast_shadows"], true),
        Range = ComponentJson.ReadFloat(raw["range"], 10.0f),
        AreaSize = ComponentJson.ReadVector2(raw["area_size"]),
        Raw = raw,
    };
}
