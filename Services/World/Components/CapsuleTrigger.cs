using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>Typed view of the engine's <c>capsule_trigger</c> component (a capsule-shaped overlap trigger).</summary>
public sealed record CapsuleTrigger : IComponentType<CapsuleTrigger>
{
    public static string Wire => "capsule_trigger";

    public float Radius { get; init; } = 0.5f;

    public float HalfHeight { get; init; } = 0.5f;

    public OverlapExecutionMode OverlapExecutionMode { get; init; } = OverlapExecutionMode.Auto;

    public bool IsOverlapEnabled { get; init; } = true;

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>
    {
        ["radius"] = ComponentJson.FloatNode(Radius),
        ["half_height"] = ComponentJson.FloatNode(HalfHeight),
        ["overlap_execution_mode"] = ComponentJson.EnumNode(OverlapExecutionMode),
        ["is_overlap_enabled"] = ComponentJson.BoolNode(IsOverlapEnabled),
    });

    public static CapsuleTrigger FromComponentJson(JObject raw) => new()
    {
        Radius = ComponentJson.ReadFloat(raw["radius"], 0.5f),
        HalfHeight = ComponentJson.ReadFloat(raw["half_height"], 0.5f),
        OverlapExecutionMode = ComponentJson.ReadEnum(raw["overlap_execution_mode"], OverlapExecutionMode.Auto),
        IsOverlapEnabled = ComponentJson.ReadBool(raw["is_overlap_enabled"], true),
        Raw = raw,
    };
}
