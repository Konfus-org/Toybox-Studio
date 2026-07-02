using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;
using Toybox.Studio.Worlds;

namespace Toybox.Studio.Ecs;

/// <summary>
/// One component on an entity, and the single base every component type derives from. It carries the
/// component's identity (its owning entity id + engine wire <see cref="Name"/>) and — through
/// <see cref="EngineSyncedObject"/> — the raw describe JSON (<see cref="EngineSyncedObject.Raw"/>, which the inspector
/// grid parses) plus the engine-sync machinery a typed subclass's <c>[EngineSync]</c> fields use. It is also the
/// behavioural handle: per-property and whole-component edits route through here, via the same
/// <see cref="EngineAddress"/> the reflected fields push through, so view-models never touch the wire. The header
/// badge icon is editor-side (the typed subclass's <c>[Icon]</c> attribute), not carried on the instance.
///
/// A wire backed by a typed C# class (<c>Transform</c>, <c>Renderer</c>, …) is that subclass — its
/// <c>[EngineSync]</c> fields are hydrated and bound; a wire with no typed class is an <see cref="Components.UnknownComponent"/>,
/// which models no fields but still carries <see cref="EngineSyncedObject.Raw"/> for the grid.
/// </summary>
public abstract class Component : EngineSyncedObject
{
    private World _world = null!;

    /// <summary>The id of the entity this component is attached to.</summary>
    public ulong EntityId { get; private set; }

    /// <summary>The component's engine wire/type name (e.g. <c>transform</c>). Set when the component is minted
    /// (by the collection converter or <see cref="Entity.GetComponent"/>), before it is attached.</summary>
    public string Name { get; internal set; } = string.Empty;

    /// <summary>Writes one property in place from its bare serialized value.</summary>
    public Task<Result> SetPropertyAsync(string property, JToken value, CancellationToken ct) =>
        SetWireAsync(property, value, ct);

    /// <summary>Resets one property to its default value.</summary>
    public Task<Result> ResetPropertyAsync(string property, CancellationToken ct) =>
        ResetWireAsync(property, ct);

    /// <summary>Removes this component from its entity.</summary>
    public Task<Result> RemoveAsync(CancellationToken ct) =>
        Structural(_world.Engine.SendCommand(
            EngineMethods.EntityRemoveComponent, new { EntityId, WorldAssetId = _world.AssetId, Component = Name }, ct));

    /// <summary>
    /// Applies a whole serialized component body to its entity, adding the component if it isn't present — the
    /// paste / restore path. Overrides the base field-seed with the structural <c>entity.setComponent</c>, so a
    /// component the entity doesn't yet carry is still taken.
    /// </summary>
    public override Task<Result> Deserialize(JObject body, CancellationToken ct = default) =>
        Structural(_world.Engine.SendCommand(
            EngineMethods.EntitySetComponent,
            new { EntityId, WorldAssetId = _world.AssetId, Component = Name, Value = body }, ct));

    /// <summary>Attaches this component to its owning entity + world, binding its reflect address so its fields
    /// push and the handle ops resolve. Called once the component is minted and its <see cref="Name"/> set;
    /// hydration of the reflected fields + <see cref="EngineSyncedObject.Raw"/> happens separately via
    /// <see cref="EngineSyncedObject.HydrateFromDescribe"/>.</summary>
    internal void Attach(ulong entityId, World world)
    {
        EntityId = entityId;
        _world = world;
        Bind(EngineAddress.World(world.AssetId).Entity(entityId).Component(Name), world.Scheduler);
    }

    // A field edit dirties (and stamps) the owning world, which owns the entity tree this component lives in.
    protected override void OnFieldEdited(string wire) => _world?.NotifyEdited();

    // Runs a structural op, dirtying the owning world on success (structural edits don't flow through the
    // reflected field path, so they signal here).
    private async Task<Result> Structural(Task<Result> op)
    {
        var result = await op.ContinueOnAnyContext();
        if (result.Success)
            _world?.NotifyEdited();
        return result;
    }
}
