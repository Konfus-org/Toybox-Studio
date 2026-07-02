using Newtonsoft.Json.Linq;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// Helpers over the engine's typed-value envelope (<c>{ "type", "value" }</c>). The one implementation of the
/// "peel the wrapper to its bare value" step the value editors and reflected codec otherwise re-inline everywhere.
/// </summary>
public static class EngineValue
{
    /// <summary>
    /// Unwraps one level of the typed-value envelope: given a <c>{ …, "value": &lt;inner&gt; }</c> wrapper returns
    /// the inner value token; given an already-bare token (or <c>null</c>) returns it unchanged.
    /// </summary>
    public static JToken? Unwrap(this JToken? token) =>
        token is JObject obj && obj.TryGetValue(EngineKeys.Value, out var inner) ? inner : token;
}
