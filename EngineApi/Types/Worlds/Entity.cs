using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types.Components;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi.Types.Worlds;

/// <summary>
/// One engine entity, mirrored. The synced scalars push through the world-qualified
/// <see cref="EngineCommands.SyncSet"/> path (<c>world/{WorldId}/entities/{Id}/{key}</c>) as they're
/// edited; <see cref="Id"/> and <see cref="Parent"/> only ever flow in from the engine
/// (<see cref="SyncMode.OneWayFromEngine"/> — a new id from a create reply, a new parent from a move); and
/// its <see cref="Components"/> are nested mirrors the sync base binds and aggregates alongside it. The
/// whole entity hydrates from one described-entity body (the shape <c>world.describe</c> answers with),
/// and the structural operations are generated engine commands.
/// </summary>
[EngineSync(EngineCommands.SyncSet, Address = "world/{WorldId}/entities/{Id}")]
public sealed partial class Entity
{
    public Entity()
    {
        Name = "Entity";
        Components = [];
    }

    /// <summary>The engine runtime id of the world this entity lives in (0 = the active editing world),
    /// which its world-qualified address routes through. The owning <see cref="World"/> stamps it before
    /// the entity binds (see <see cref="EngineObject.PrepareChild"/>); routing only, never synced.</summary>
    public ulong WorldId { get; internal set; }

    /// <summary>The engine-assigned id; engine → studio only, so the entity only ever assigns it locally
    /// (from a create reply).</summary>
    [EngineSync(EngineCommands.SyncSet, SyncMode.OneWayFromEngine)]
    public partial ulong Id { get; private set; }

    [EngineSync(EngineCommands.SyncSet)]
    public partial string Name { get; set; }

    [EngineSync(EngineCommands.SyncSet)]
    public partial bool IsEnabled { get; set; }

    /// <summary>The entity's sibling order; batched wide so a drag-reorder settles before pushing.</summary>
    [EngineSync(EngineCommands.SyncSet, SyncMode.Batched, batchFrequencyMs: 250)]
    public partial int Order { get; set; }

    /// <summary>The owning (parent) entity's id, or 0 at the world root; engine → studio only —
    /// re-parenting is a structural op (<c>entity.move</c>), not a synced field write.</summary>
    [EngineSync(EngineCommands.SyncSet, SyncMode.OneWayFromEngine)]
    public partial ulong Parent { get; private set; }

    /// <summary>Whether the entity is resident in the world globals rather than a streamed chunk. World-
    /// level state the describe layer tags each entity with; toggling it pushes through
    /// <c>entity.set</c>'s <c>is_global</c> handler.</summary>
    [EngineSync(EngineCommands.SyncSet)]
    public partial bool IsGlobal { get; set; }

    /// <summary>The entity's components, keyed on the wire by name — engine → studio (membership is a
    /// structural op; each component's own edits push through the world-qualified
    /// <see cref="EngineCommands.SyncSet"/> path). The sync base binds them at
    /// <c>world/{WorldId}/entities/{Id}/components/{name}</c> and bubbles their edits up.</summary>
    [EngineSync(EngineCommands.SyncSet, SyncMode.OneWayFromEngine, typeof(ComponentListConverter))]
    public partial IReadOnlyList<Component> Components { get; private set; }

    /// <summary>Asks the engine to clone this entity (children included).</summary>
    [EngineSync(EngineCommands.EntityDuplicate)]
    public partial Task<Result> DuplicateAsync(CancellationToken ct = default);

    /// <summary>Asks the engine to destroy this entity and its children.</summary>
    [EngineSync(EngineCommands.EntityDestroy)]
    public partial Task<Result> DestroyAsync(CancellationToken ct = default);

    /// <summary>Asks the engine to reparent this entity under <paramref name="parent"/> (0 = the world root)
    /// at sibling <paramref name="index"/> — the structural op behind a drag reorder/reparent. Both the
    /// reorder (same parent, new index) and the reparent (new parent) are this one call; the engine renumbers
    /// the siblings and streams the new <see cref="Parent"/> / <see cref="Order"/> back.</summary>
    [EngineSync(EngineCommands.EntityMove)]
    public partial Task<Result> MoveAsync(ulong parent, int index, CancellationToken ct = default);

    /// <summary>Asks the engine to attach a component by its registered name.</summary>
    [EngineSync(EngineCommands.EntityAddComponent)]
    public partial Task<Result> AddComponentAsync(string component, CancellationToken ct = default);

    /// <summary>Sets the entity's enabled flag and flushes the change to the engine. <see cref="IsEnabled"/>
    /// is an ordinary synced setter, so this just writes it and commits any pending push — the small async
    /// wrapper the world menu / toolbar await so an "Enable / Disable" toggle reports a wire failure.</summary>
    public async Task<Result> SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        IsEnabled = enabled;
        return await CommitAsync(ct).ContinueOnAnyContext();
    }

    /// <summary>A component addresses itself relative to this entity (<c>{EntityAddress}/components/{Name}</c>),
    /// so stamp this entity's own world-qualified address onto it before it binds — the component points to
    /// its entity, and the entity to its world. Its <see cref="Component.EntityId"/> and
    /// <see cref="Component.Name"/> were set from the entity id and the wire key it was read under.</summary>
    protected override void PrepareChild(EngineObject child)
    {
        if (child is Component component)
        {
            component.EntityId = Id;
            component.EntityAddress = Address;
        }
    }

    /// <summary>Reverts this entity to an undo snapshot body: its scalar deltas push back through the
    /// entity (name / enabled / order / global), and each component's deltas through the matching
    /// component mirror. Only real deltas reach the wire. A component the snapshot names that the entity
    /// no longer carries (a structural change) is left to the structural pass.</summary>
    internal void RestoreSnapshot(JObject body)
    {
        RestoreFrom(body);
        if (body["components"] is not JObject components)
            return;

        foreach (var component in Components)
            if (components[component.Name] is JObject componentBody)
                component.RestoreFrom(componentBody);
    }

    /// <summary>The diff twin of <see cref="RestoreSnapshot"/> for undo/redo: the entity's scalar deltas
    /// diff through the base, and each component present in both snapshots restores only the values this
    /// step changed (its transform, for a gizmo drag) — leaving components and fields the edit never
    /// touched alone.</summary>
    internal void RestoreSnapshotDiff(JObject target, JObject from)
    {
        RestoreDiff(target, from);
        if (target["components"] is not JObject targetComponents)
            return;

        var fromComponents = from["components"] as JObject;
        foreach (var component in Components)
            if (targetComponents[component.Name] is JObject componentTarget)
            {
                var componentFrom = fromComponents?[component.Name] as JObject ?? new JObject();
                if (!JToken.DeepEquals(componentTarget, componentFrom))
                    component.RestoreDiff(componentTarget, componentFrom);
            }
    }
}
