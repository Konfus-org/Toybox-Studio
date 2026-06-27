using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>Typed view of the engine's <c>box_trigger</c> component (a box-shaped overlap trigger).</summary>
public sealed record BoxTrigger : IComponentType<BoxTrigger>
{
    public static string Wire => "box_trigger";

    public Vector3 HalfExtents { get; init; } = new(0.5f, 0.5f, 0.5f);

    public OverlapExecutionMode OverlapExecutionMode { get; init; } = OverlapExecutionMode.Auto;

    public bool IsOverlapEnabled { get; init; } = true;

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>
    {
        ["half_extents"] = ComponentJson.Vec3Node(HalfExtents),
        ["overlap_execution_mode"] = ComponentJson.EnumNode(OverlapExecutionMode),
        ["is_overlap_enabled"] = ComponentJson.BoolNode(IsOverlapEnabled),
    });

    public static BoxTrigger FromComponentJson(JObject raw) => new()
    {
        HalfExtents = ComponentJson.ReadVector3(raw["half_extents"]),
        OverlapExecutionMode = ComponentJson.ReadEnum(raw["overlap_execution_mode"], OverlapExecutionMode.Auto),
        IsOverlapEnabled = ComponentJson.ReadBool(raw["is_overlap_enabled"], true),
        Raw = raw,
    };
}
