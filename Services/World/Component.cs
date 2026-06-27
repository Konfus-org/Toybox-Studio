using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.World;

/// <summary>
/// A handle to one component on one <see cref="Entity"/>: it knows the <see cref="Entity"/> it belongs to
/// and the component's type name, and fronts the engine's per-component and per-property RPCs so view-models
/// never touch the wire. Cheap to create on demand. Obtained from <see cref="Entity.Component"/>; its data
/// counterpart is the <see cref="ComponentDescription"/> the property grid binds to.
/// </summary>
public sealed class Component
{
    private readonly Entity _entity;

    internal Component(Entity entity, string name)
    {
        _entity = entity;
        Name = name;
    }

    /// <summary>The entity this component is attached to.</summary>
    public Entity Entity => _entity;

    /// <summary>The owning entity's id.</summary>
    public ulong EntityId => _entity.Id;

    /// <summary>The component's type name (e.g. <c>transform</c>).</summary>
    public string Name { get; }

    private EngineRpc Engine => _entity.World.Engine;

    /// <summary>
    /// Reads this component's whole serialized body as JSON (for copy/paste and any "give me the JSON"
    /// flow). The body round-trips through <see cref="SetAsync"/>.
    /// </summary>
    public async Task<Result<JObject>> ReadAsync(CancellationToken ct = default)
    {
        var result = await Engine
            .InvokeAsync<JObject>("entity.describe", new { EntityId }, ct).ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
            return Result<JObject>.Fail(result.Error ?? "The engine returned no entity.");

        return reply["entity"]?["components"]?[Name] is JObject body
            ? Result<JObject>.Ok(body)
            : Result<JObject>.Fail($"Entity has no '{Name}' component.");
    }

    /// <summary>Replaces this whole component with the given typed JSON (adds it if absent).</summary>
    public Task<Result> SetAsync(JObject value, CancellationToken ct) =>
        Engine.InvokeAsync(
            "entity.setComponent",
            new { EntityId, Component = Name, Value = value },
            ct);

    /// <summary>Removes this component from its entity.</summary>
    public Task<Result> RemoveAsync(CancellationToken ct) =>
        Engine.InvokeAsync(
            "entity.removeComponent",
            new { EntityId, Component = Name },
            ct);

    /// <summary>Reads one property as a self-describing <c>{ type, value }</c> node.</summary>
    public Task<Result<JObject>> GetPropertyAsync(string property, CancellationToken ct) =>
        Engine.InvokeAsync<JObject>(
            "reflect.get",
            new { EntityId, Component = Name, Property = property },
            ct);

    /// <summary>Writes one property in place from its bare serialized value.</summary>
    public Task<Result> SetPropertyAsync(string property, JToken value, CancellationToken ct) =>
        Engine.InvokeAsync(
            "reflect.set",
            new { EntityId, Component = Name, Property = property, Value = value },
            ct);

    /// <summary>Resets one property to its default value.</summary>
    public Task<Result> ResetPropertyAsync(string property, CancellationToken ct) =>
        Engine.InvokeAsync(
            "reflect.reset",
            new { EntityId, Component = Name, Property = property },
            ct);
}
