using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>Typed view of the engine's <c>mesh_trigger</c> component (sourced from the entity's model).</summary>
public sealed record MeshTrigger : IComponentType<MeshTrigger>
{
    public static string Wire => "mesh_trigger";

    public bool IsConvex { get; init; } = true;

    public OverlapExecutionMode OverlapExecutionMode { get; init; } = OverlapExecutionMode.Auto;

    public bool IsOverlapEnabled { get; init; } = true;

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>
    {
        ["is_convex"] = ComponentJson.BoolNode(IsConvex),
        ["overlap_execution_mode"] = ComponentJson.EnumNode(OverlapExecutionMode),
        ["is_overlap_enabled"] = ComponentJson.BoolNode(IsOverlapEnabled),
    });

    public static MeshTrigger FromComponentJson(JObject raw) => new()
    {
        IsConvex = ComponentJson.ReadBool(raw["is_convex"], true),
        OverlapExecutionMode = ComponentJson.ReadEnum(raw["overlap_execution_mode"], OverlapExecutionMode.Auto),
        IsOverlapEnabled = ComponentJson.ReadBool(raw["is_overlap_enabled"], true),
        Raw = raw,
    };
}
