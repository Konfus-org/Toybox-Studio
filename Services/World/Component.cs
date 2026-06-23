using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.World;

/// <summary>
/// The editor-side handle to one component on one entity: a thin, identity-only construct (entity id +
/// component name) that fronts the engine's per-component and per-property RPCs so view-models never touch the
/// wire. Cheap to create on demand — it carries no state, just the address of the component and the shared
/// <see cref="EngineRpc"/> transport. Obtained from <see cref="Entity.Component"/>; its data counterpart is the
/// <see cref="ComponentDescription"/> the property grid binds to.
/// </summary>
public sealed class Component
{
    private readonly EngineRpc _engine;

    internal Component(EngineRpc engine, ulong entityId, string name)
    {
        _engine = engine;
        EntityId = entityId;
        Name = name;
    }

    /// <summary>The owning entity's id.</summary>
    public ulong EntityId { get; }

    /// <summary>The component's type name (e.g. <c>transform</c>).</summary>
    public string Name { get; }

    /// <summary>Replaces this whole component with the given typed JSON.</summary>
    public Task<Result> SetAsync(JObject value, CancellationToken ct) =>
        _engine.InvokeAsync(
            "entity.setComponent",
            new { EntityId, Component = Name, Value = value },
            ct);

    /// <summary>Reads one property as a self-describing <c>{ type, value }</c> node.</summary>
    public Task<Result<JObject>> GetPropertyAsync(string property, CancellationToken ct) =>
        _engine.InvokeAsync<JObject>(
            "reflect.get",
            new { EntityId, Component = Name, Property = property },
            ct);

    /// <summary>Writes one property in place from its bare serialized value.</summary>
    public Task<Result> SetPropertyAsync(string property, JToken value, CancellationToken ct) =>
        _engine.InvokeAsync(
            "reflect.set",
            new { EntityId, Component = Name, Property = property, Value = value },
            ct);

    /// <summary>Resets one property to its default value.</summary>
    public Task<Result> ResetPropertyAsync(string property, CancellationToken ct) =>
        _engine.InvokeAsync(
            "reflect.reset",
            new { EntityId, Component = Name, Property = property },
            ct);

    /// <summary>Asks whether one property currently equals its default value.</summary>
    public async Task<Result<bool>> IsPropertyDefaultAsync(string property, CancellationToken ct)
    {
        var result = await _engine.InvokeAsync<JObject>(
            "reflect.isDefault",
            new { EntityId, Component = Name, Property = property },
            ct).ContinueOnAnyContext();
        return result is { Success: true, Value: { } reply }
            ? Result<bool>.Ok(reply.Value<bool>("isDefault"))
            : Result<bool>.Fail(result.Error ?? "The engine returned no result.");
    }
}
