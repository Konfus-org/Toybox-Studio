using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>
/// Typed handle for the engine's <c>post_processing</c> component (the effect stack). The effect list is
/// structured, so it round-trips whole through <see cref="Raw"/> and is edited via the dynamic component
/// path; the typed handle lets code address it string-free.
/// </summary>
public sealed record PostProcessing : IComponentType<PostProcessing>
{
    public static string Wire => "post_processing";

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>());

    public static PostProcessing FromComponentJson(JObject raw) => new() { Raw = raw };
}
