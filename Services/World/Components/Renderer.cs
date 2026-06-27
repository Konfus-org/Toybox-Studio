using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Project;

namespace Toybox.Studio.Services.World.Components;

/// <summary>
/// Typed view of the engine's <c>renderer</c> component: a <see cref="Model"/> asset plus per-slot
/// <see cref="Materials"/> overrides (each a MaterialInstance/Material asset handle, aligned 1:1 with the
/// model's slots; an empty handle inherits the model's own).
/// </summary>
public sealed record Renderer : IComponentType<Renderer>
{
    public static string Wire => "renderer";

    /// <summary>The model asset providing geometry + default material slots.</summary>
    public AssetHandle Model { get; init; } = AssetHandle.None;

    /// <summary>Per-slot material overrides (asset handles); shorter than the slot count inherits the rest.</summary>
    public IReadOnlyList<AssetHandle> Materials { get; init; } = [];

    /// <summary>The component body this was read from, so untyped fields round-trip on write (null when fresh).</summary>
    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>
    {
        ["model"] = ComponentJson.HandleNode(Model),
        ["materials"] = ComponentJson.HandleArrayNode(Materials),
    });

    public static Renderer FromComponentJson(JObject raw) => new()
    {
        Model = ComponentJson.ReadHandle(raw["model"]),
        Materials = ComponentJson.ReadHandles(raw["materials"]),
        Raw = raw,
    };
}
