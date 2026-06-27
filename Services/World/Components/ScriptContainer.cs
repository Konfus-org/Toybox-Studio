using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>
/// Typed handle for the engine's <c>script_container</c> component (the entity's script bindings). The
/// binding list is structured, so it round-trips whole through <see cref="Raw"/> and is edited via the
/// dynamic path / the dedicated script editor; the typed handle lets code address it string-free.
/// </summary>
public sealed record ScriptContainer : IComponentType<ScriptContainer>
{
    public static string Wire => "script_container";

    public JObject? Raw { get; init; }

    public JObject ToComponentJson() => ComponentJson.Merge(Raw, new Dictionary<string, JToken>());

    public static ScriptContainer FromComponentJson(JObject raw) => new() { Raw = raw };
}
