using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.World.Components;

/// <summary>
/// A typed, string-free view of an engine component. Implementations are small hand-written records (the
/// editor's own curated abstraction, like Unity's <c>Transform</c>/<c>Rigidbody</c>) that carry the
/// component's wire name ONCE — in <see cref="Wire"/> — so generic calls such as
/// <c>entity.GetComponentAsync&lt;Renderer&gt;()</c> never spell a component name at the call site.
///
/// The pair of (de)serializers map the typed fields to/from the engine's self-describing
/// <c>{ type, value }</c> JSON. Components the editor has no typed view for are still fully editable through
/// the dynamic <see cref="Component"/> path, so this layer only ever ADDS compile-time ergonomics — it never
/// gates functionality.
/// </summary>
/// <typeparam name="TSelf">The implementing record itself (CRTP), so the static factory can return it.</typeparam>
public interface IComponentType<out TSelf>
    where TSelf : IComponentType<TSelf>
{
    /// <summary>The engine component type name (e.g. <c>"renderer"</c>).</summary>
    static abstract string Wire { get; }

    /// <summary>Reconstructs the typed component from a component body (the <c>Raw</c> JObject of a
    /// describe snapshot, whose fields are <c>{ value, … }</c> nodes).</summary>
    static abstract TSelf FromComponentJson(JObject raw);

    /// <summary>Serializes this component to the engine's per-field typed JSON for <c>entity.setComponent</c>.</summary>
    JObject ToComponentJson();
}
