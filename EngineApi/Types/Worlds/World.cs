using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types.Components;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi.Types.Worlds;

/// <summary>
/// A <c>.world</c> asset, mirrored from the engine's <c>World</c>: the <see cref="WorldGlobals"/> handle
/// plus the <see cref="WorldChunk"/> handles it streams (its own serialized body), and — once it is the
/// active editing world — the live <see cref="Entities"/> registry it hosts. Entities and their components
/// are nested <see cref="EngineObject"/> mirrors: the sync base binds them alongside the world, bubbles
/// their edits into its <see cref="Asset.IsDirty"/> flag, and drops them when the world is disposed. The
/// registry is read whole from a single <c>sync.describe</c> on the world's address (which the engine
/// answers with the optimized <c>world.describe</c> — every entity with its full components and is-global
/// flag), and saved with the world through <c>asset.save</c> (the engine routes a world's save to its
/// chunk + globals files).
/// </summary>
public sealed partial class World : Asset
{
    public World()
    {
        Chunks = [];
        Entities = [];
    }

    /// <summary>The engine runtime id this world's entities address through (<c>world/{WorldId}/…</c>):
    /// <c>0</c> for the active editing world (the engine resolves an id-0 address against its active
    /// world), or the id a <c>world.load</c> / preview view assigned to a loaded non-active world. The
    /// owner sets it before <see cref="EngineObject.Bind"/>; routing only, never synced.</summary>
    public ulong WorldId { get; set; }

    /// <summary>The active editing world by its <c>.world</c> asset id: the base load hydrates and binds
    /// the asset body, and <see cref="EngineObject.RefreshAsync"/> reads the entity registry.</summary>
    public World(ulong id) : base(id)
    {
        Chunks = [];
        Entities = [];
    }

    /// <summary>The full-lifetime resident entities (a <c>.globals</c> asset).</summary>
    [EngineSync]
    public partial Handle Globals { get; set; }

    /// <summary>The streamable spatial chunks (<c>.chunk</c> assets).</summary>
    [EngineSync(Converter = typeof(HandleListConverter))]
    public partial IReadOnlyList<Handle> Chunks { get; set; }

    /// <summary>The world's live entities (flat; each entity's <see cref="Entity.Parent"/> describes the
    /// hierarchy) — engine → studio, hydrated from the world describe. The sync base binds each entity (and
    /// its components) and folds their edits into the world's dirty flag; membership changes are structural
    /// ops (<c>entity.create</c> / <c>entity.destroy</c>), not a push of the whole list.</summary>
    [EngineSync(Mode = SyncMode.OneWayFromEngine, Converter = typeof(EntityListConverter))]
    public partial IReadOnlyList<Entity> Entities { get; private set; }

    /// <summary>The live entity with this id, or null — the selection tracks ids, so panels resolve them
    /// against the current registry (which a refresh rebuilds).</summary>
    public Entity? Find(ulong id) => Entities.FirstOrDefault(entity => entity.Id == id);

    /// <summary>
    /// Spawns a fresh entity in this world (engine <c>entity.create</c>), then re-reads the registry so the
    /// new entity appears in <see cref="Entities"/>, and returns it. The engine appends it after its last
    /// sibling. <paramref name="parent"/> 0 places it at the world root.
    /// </summary>
    public async Task<Result<Entity>> CreateEntityAsync(
        string name, ulong parent, CancellationToken ct = default)
    {
        var payload = new JObject { ["name"] = name };
        if (parent != 0UL)
            payload["parent"] = parent;
        if (WorldId != 0UL)
            payload["worldAssetId"] = WorldId; // 0 = the active editing world, which the engine resolves itself

        var created = await SendQueryAsync(
                EngineCommands.EntityCreate, payload, token => token.Value<ulong?>("id") ?? 0UL, ct)
            .ContinueOnAnyContext();
        if (!created)
            return Result<Entity>.Fail(created.Error!);
        if (created.Value == 0UL)
            return Result<Entity>.Fail("The engine did not return a new entity id.");

        await RefreshAsync(ct).ContinueOnAnyContext();
        return Find(created.Value) is { } entity
            ? Result<Entity>.Ok(entity)
            : Result<Entity>.Fail("The created entity did not appear in the world.");
    }

    /// <summary>Adds an entity at the world root, streamed or global (the world tree's "Add Entity" /
    /// "Add Global Entity"). Global-ness is a synced entity flag, so it is set and flushed after the spawn.</summary>
    public async Task<Result<Entity>> AddEntityAsync(bool global, CancellationToken ct = default)
    {
        var created = await CreateEntityAsync("Entity", parent: 0UL, ct).ContinueOnAnyContext();
        if (!created || created.Value is not { } entity || !global)
            return created;

        entity.IsGlobal = true;
        await entity.CommitAsync(ct).ContinueOnAnyContext();
        await RefreshAsync(ct).ContinueOnAnyContext();
        return Find(entity.Id) is { } refreshed ? Result<Entity>.Ok(refreshed) : created;
    }

    /// <summary>Destroys the given entities (each with its descendants, engine-side), then re-reads the
    /// registry once so the tree drops them together.</summary>
    public async Task<Result> DeleteAsync(IReadOnlyList<Entity> entities, CancellationToken ct = default)
    {
        foreach (var entity in entities)
        {
            var result = await entity.DestroyAsync(ct).ContinueOnAnyContext();
            if (!result)
                return result;
        }

        await RefreshAsync(ct).ContinueOnAnyContext();
        return Result.Ok();
    }

    /// <summary>Clones the given entities (children included), then re-reads the registry so the copies
    /// appear.</summary>
    public async Task<Result> DuplicateAsync(IReadOnlyList<Entity> entities, CancellationToken ct = default)
    {
        foreach (var entity in entities)
        {
            var result = await entity.DuplicateAsync(ct).ContinueOnAnyContext();
            if (!result)
                return result;
        }

        await RefreshAsync(ct).ContinueOnAnyContext();
        return Result.Ok();
    }

    /// <summary>Moves the given entities between the streamed and global buckets (a synced entity flag),
    /// flushes each, then re-reads the registry so the tree re-sorts them.</summary>
    public async Task<Result> SetGlobalAsync(
        IReadOnlyList<Entity> entities, bool global, CancellationToken ct = default)
    {
        foreach (var entity in entities)
        {
            entity.IsGlobal = global;
            var result = await entity.CommitAsync(ct).ContinueOnAnyContext();
            if (!result)
                return result;
        }

        await RefreshAsync(ct).ContinueOnAnyContext();
        return Result.Ok();
    }

    /// <summary>Reparents/reorders an entity: optionally flips its streamed/global bucket first (a
    /// promote/demote drag), moves it under <paramref name="parent"/> at sibling <paramref name="index"/>
    /// (engine <c>entity.move</c> — the reorder and the reparent are one op), then re-reads the registry so
    /// the tree re-sorts. <paramref name="parent"/> 0 places it at the world root.</summary>
    public async Task<Result> MoveEntityAsync(
        Entity entity, ulong parent, int index, bool? global = null, CancellationToken ct = default)
    {
        if (global is { } wantGlobal && wantGlobal != entity.IsGlobal)
        {
            entity.IsGlobal = wantGlobal;
            var flip = await entity.CommitAsync(ct).ContinueOnAnyContext();
            if (!flip)
                return flip;
        }

        var moved = await entity.MoveAsync(parent, index, ct).ContinueOnAnyContext();
        if (!moved)
            return moved;

        await RefreshAsync(ct).ContinueOnAnyContext();
        return Result.Ok();
    }

    /// <summary>Each entity addresses itself through this world's runtime id
    /// (<c>world/{WorldId}/entities/{Id}</c>), so stamp it onto every entity before it binds; the entity in
    /// turn stamps its own address onto its components. Called just before each child binds.</summary>
    protected override void PrepareChild(EngineObject child)
    {
        if (child is Entity entity)
            entity.WorldId = WorldId;
    }

    /// <summary>
    /// Reverts the world to an undo snapshot: the world's own scalar body pushes through the base, and each
    /// snapshotted entity's edits reconcile onto the live entity of the same id (its scalars and every
    /// component's values). Only real deltas reach the wire (the setters' own equality test). This is the
    /// property-level reconcile; structural differences (an entity the snapshot has and the live world no
    /// longer does, or vice versa) are left for the structural pass — restoring them means recreating or
    /// destroying entities engine-side and re-reading the registry, which this doesn't yet do.
    /// </summary>
    public override void RestoreFrom(JObject body)
    {
        base.RestoreFrom(body);
        if (body["entities"] is not JArray snapshot)
            return;

        var live = Entities.ToDictionary(entity => entity.Id);
        foreach (var entityBody in snapshot.OfType<JObject>())
            if (live.TryGetValue(WireValue.ReadUInt64(WireValue.Field(entityBody, "id")), out var entity))
                entity.RestoreSnapshot(entityBody);
    }

    /// <summary>
    /// The diff twin of <see cref="RestoreFrom"/> for undo/redo: pushes only what the step changed. The
    /// world's own scalar body (globals, chunks) diffs through the base, and each entity present in both
    /// snapshots reconciles just its changed scalars and components. Crucially it never drives back the
    /// world's engine-owned handles or an untouched entity's transform when they merely differ from the
    /// live mirror — a whole-body restore would, corrupting the scene (an entity's material handle the
    /// describe never carried, the world sky) on every undo. Structural differences (an entity one snapshot
    /// has and the other doesn't) are left to a future structural pass, as with <see cref="RestoreFrom"/>.
    /// </summary>
    public override void RestoreDiff(JObject target, JObject from)
    {
        // The world's own scalars diff through the base, but "entities" is a child list reconciled per
        // entity below — never pushed as a single value.
        foreach (var (key, value) in target)
            if (key != "entities" && !JToken.DeepEquals(value, from[key])
                && WireValue.Unwrap(value) is { } bare)
                PushApply(key, bare);

        if (target["entities"] is not JArray targetEntities)
            return;

        var fromEntities = (from["entities"] as JArray)?.OfType<JObject>()
            .ToDictionary(entity => WireValue.ReadUInt64(WireValue.Field(entity, "id")));
        var live = Entities.ToDictionary(entity => entity.Id);
        foreach (var entityBody in targetEntities.OfType<JObject>())
        {
            var id = WireValue.ReadUInt64(WireValue.Field(entityBody, "id"));
            if (!live.TryGetValue(id, out var entity))
                continue;

            var fromBody = fromEntities is not null && fromEntities.TryGetValue(id, out var found)
                ? found
                : new JObject();
            if (JToken.DeepEquals(entityBody, fromBody))
                continue;

            entity.RestoreSnapshotDiff(entityBody, fromBody);
        }
    }
}
