using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>
/// Typed handle for the engine's <c>camera</c> component. Its fields (render target, viewport, projection)
/// are complex, so it round-trips whole through <see cref="Raw"/> and is edited via the dynamic component
/// path; the typed handle just lets code address it string-free (<c>entity.GetComponentAsync&lt;Camera&gt;()</c>).
/// </summary>
public sealed record Camera : IComponentType<Camera>
{
    public static string Wire => "camera";

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>());

    public static Camera FromComponentJson(JObject raw) => new() { Raw = raw };
}
