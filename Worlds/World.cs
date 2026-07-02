using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;
using Toybox.Studio.Project.Assets;
using Toybox.Studio.Utils;
using Toybox.Studio.Ecs;

namespace Toybox.Studio.Worlds;

/// <summary>
/// One engine world — the root of the editor's runtime-like object graph (<c>World</c> →
/// <see cref="Entity"/> → <see cref="Component"/>), and the typed data of a <c>.world</c> asset. A world IS asset
/// data (an <see cref="AssetData"/>): its on-disk reference fields — the <see cref="Globals"/> handle and the
/// <see cref="Chunks"/> handle list — are reflected, so a <c>.world</c> file routes through the same
/// <c>Asset&lt;World&gt;</c> machinery (file lifecycle, inspect) as any other asset. Beyond that reference data a
/// LIVE world (built through <see cref="ForActive"/> / <see cref="ForPreview"/>) also drives the entity tree: its
/// <see cref="Entities"/> are a reflected collection, hydrated from <c>world.describe</c> through the
/// <see cref="EntityCollectionConverter"/> and persisted through <c>world.save</c>. Dirty tracking is automatic
/// (inherited from <see cref="AssetData"/>): any reflected edit anywhere in the tree marks the world dirty.
///
/// A live world is addressed over RPC by its <see cref="AssetId"/> (no separate "world id"): <c>0</c> is the active
/// editing world (it streams into the <see cref="StreamDestination.Game"/> viewports), a non-zero id is a preview
/// world's asset (an isolated copy the asset viewer drives, streaming into a <see cref="StreamDestination.Preview"/>
/// world). Per-entity ops carry it so the engine resolves the right world.
/// </summary>
// Only the .world reference file maps to this type; its .globals/.chunk parts are their own assets
// (Asset<Globals>/Asset<Chunk>), routed by the factory to the typed data mirrors in Project/Assets.
[AssetInfo("world")]
public sealed partial class World : AssetData
{
    private readonly Dictionary<ulong, Entity> _byId = [];

    // The on-disk .world reference: the globals asset and the chunk assets it composes. Reflected so an
    // Asset<World> file handle hydrates/saves them; a live world leaves them default and drives via world.* instead.
    [EngineSync] private AssetHandle _globals;
    [EngineSync] private IReadOnlyList<AssetHandle> _chunks = [];

    // The live world's entity tree, reflected via a converter that builds the typed Entity/Component tree from the
    // engine describe. Hydrate-only: the collection isn't pushed wholesale (live edits go per-field, structural
    // changes through entity.* verbs, disk save through world.save); it's re-pulled by RefreshAsync.
    [EngineSync(converter: typeof(EntityCollectionConverter), mode: EngineSyncMode.Hydrate)]
    private IReadOnlyList<Entity> _entities = [];

    // The engine transport + reflect scheduler a LIVE world (active/preview) drives its entity tree through, set by
    // the live constructor (from GameState / the asset viewer) — NOT the asset-services bundle: a live world is
    // built inside GameState, and pulling AssetServices (which owns AssetOpener → GameState) would form a
    // construction cycle. A factory-built Asset<World> data payload uses the parameterless ctor and never touches
    // these.
    private readonly Engine _engine = null!;
    private readonly IEngineSyncScheduler _scheduler = null!;

    // Gates dirty: a world edited while the game is playing hits the engine's throwaway play snapshot, so it isn't
    // a real change. Supplied by GameState for the active world; null (never playing) for previews.
    private Func<bool>? _isPlaying;

    /// <summary>The parameterless data-payload constructor an <c>Asset&lt;World&gt;</c> file handle uses (its
    /// entity tree is never driven — only the reflected globals/chunks reference fields). A live world uses the
    /// private transport-wiring constructor via <see cref="ForActive"/> / <see cref="ForPreview"/>.</summary>
    public World() => PropertyChanged += OnSelfChanged;

    // Wires a LIVE world (active/preview): its engine transport, reflect scheduler, RPC addressing id and owner,
    // binding it to its stream destination so reflected edits push to the right running world. Constructors over
    // an init method, per house style; ForActive/ForPreview are the named entry points.
    private World(
        Engine engine, IEngineSyncScheduler scheduler, ulong assetId,
        StreamDestination destination, object? owner)
        : this()
    {
        _engine = engine;
        _scheduler = scheduler;
        AssetId = assetId;
        Owner = owner;
        Bind(EngineAddress.Stream(destination, assetId), scheduler);
    }

    /// <summary>The RPC addressing id for this world: <c>0</c> = the active editing world; non-zero = a
    /// preview world's asset id. Sent with every per-entity op so the engine targets the right world.</summary>
    public ulong AssetId { get; private set; }

    /// <summary>Who owns this world editor-side (the shell/GameState for the active world; an asset viewer for
    /// a preview).</summary>
    public object? Owner { get; private set; }

    /// <summary>Raised with the new root set whenever the world's entity tree changes.</summary>
    public event Action<IReadOnlyList<Entity>>? Changed;

    /// <summary>Raised whenever a reflected edit anywhere in the tree is applied — so the editor can defer its
    /// live play-mode pull to a value the user just changed (the world already dirtied itself).</summary>
    public event Action? Edited;

    // The live world's transport + scheduler, exposed within the assembly so the entities and components this
    // world vends reach the engine without each holding their own references.
    internal Engine Engine => _engine;

    internal IEngineSyncScheduler Scheduler => _scheduler;

    // A world edited while the game plays hits the engine's throwaway play snapshot, so those edits don't dirty it.
    protected override bool CanDirty => _isPlaying?.Invoke() != true;

    /// <summary>Mints the active editing world (addressed as id 0), owned by <paramref name="owner"/>, wired to
    /// the live transport/scheduler its entity tree uses.</summary>
    internal static World ForActive(
        object? owner, Engine engine, IEngineSyncScheduler scheduler) =>
        new(engine, scheduler, assetId: 0, StreamDestination.Game, owner);

    /// <summary>A handle to an isolated preview world by its world-asset handle, owned editor-side by
    /// <paramref name="owner"/> (e.g. the asset viewer), wired to the live transport/scheduler its entity
    /// tree uses. Per-entity ops carry its <see cref="AssetId"/>.</summary>
    public static World ForPreview(
        AssetHandle previewWorld, object? owner, Engine engine, IEngineSyncScheduler scheduler) =>
        new(engine, scheduler, previewWorld.Id, StreamDestination.Preview, owner);

    /// <summary>A cheap identity handle to one entity in this world (attached but with no subtree).</summary>
    public Entity GetEntity(ulong id)
    {
        var entity = new Entity(id);
        entity.Attach(this);
        return entity;
    }

    /// <summary>The live tree node for an id, or null when this world doesn't contain it.</summary>
    public Entity? Find(ulong id) => _byId.GetValueOrDefault(id);

    /// <summary>The live tree node for a handle, or null when this world doesn't contain it.</summary>
    public Entity? Find(EntityHandle handle) => handle.IsNone ? null : Find(handle.Id);

    /// <summary>Sets the play-state gate (so edits while playing don't dirty the world). GameState supplies it
    /// for the active world.</summary>
    internal void GuardDirtyWhilePlaying(Func<bool> isPlaying) => _isPlaying = isPlaying;

    // A reflected edit anywhere in the tree (an entity/component bubbling up) dirties the world and stamps the
    // edit for the play-pull guard. Also the seam World's own reflected reference fields dirty through.
    internal void NotifyEdited()
    {
        MarkDirty();
        Edited?.Invoke();
    }

    /// <summary>
    /// Creates a new entity in THIS world (optionally named and parented; a zero parent means a root entity)
    /// and returns a handle to it, marking the world dirty. The engine appends it after its last sibling. The
    /// caller refreshes.
    /// </summary>
    public async Task<Result<Entity>> CreateEntityAsync(string? name, ulong parent, CancellationToken ct)
    {
        var result = await Engine
            .SendCommand<JObject>(
                EngineMethods.EntityCreate, new { Name = name ?? string.Empty, Parent = parent, WorldAssetId = AssetId }, ct)
            .ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
            return Result<Entity>.Fail(result.Error ?? "The engine returned no result.");

        NotifyEdited();
        return Result<Entity>.Ok(GetEntity(reply.Value<ulong>("id")));
    }

    /// <summary>
    /// Adds a new entity at the root of this world (optionally promoted to global). The caller refreshes +
    /// selects (and may drop it into inline rename) — this performs no implicit re-pull.
    /// </summary>
    public async Task<Result<Entity>> AddEntityAsync(bool global, CancellationToken ct = default)
    {
        var created = await CreateEntityAsync("Entity", parent: 0UL, ct).ContinueOnAnyContext();
        if (created is not { Success: true, Value: { } entity })
            return created;

        if (global)
        {
            var promote = await entity.SetGlobalAsync(true, ct).ContinueOnAnyContext();
            if (!promote.Success)
                return Result<Entity>.Fail(promote.Error!);
        }

        return Result<Entity>.Ok(entity);
    }

    /// <summary>Destroys each entity (and its subtree) then re-syncs once. Stops at the first failure.
    /// Entity delegates its single-entity ops here so a multi-entity caller pays one refresh; the menus call
    /// these directly for a multi-selection.</summary>
    public async Task<Result> DeleteAsync(IEnumerable<Entity> entities)
    {
        var result = Result.Ok();
        foreach (var entity in entities.ToList())
        {
            result = await entity.DestroyAsync(CancellationToken.None).ContinueOnAnyContext();
            if (!result.Success)
                break;
        }

        await RefreshAsync().ContinueOnAnyContext();
        return result;
    }

    /// <summary>Duplicates each entity (a fresh-id copy under the same parent) then re-syncs once.</summary>
    public async Task<Result> DuplicateAsync(IEnumerable<Entity> entities)
    {
        var result = Result.Ok();
        foreach (var entity in entities.ToList())
        {
            var created = await CreateEntityAsync(entity.Name, entity.ParentId, CancellationToken.None)
                .ContinueOnAnyContext();
            if (!created.Success || created.Value is not { } copy)
            {
                result = Result.Fail(created.Error!);
                break;
            }

            result = await copy.Deserialize(entity.Serialize(), CancellationToken.None).ContinueOnAnyContext();
            if (!result.Success)
                break;
        }

        await RefreshAsync().ContinueOnAnyContext();
        return result;
    }

    /// <summary>Promotes/demotes each entity between global and streamed then re-syncs once.</summary>
    public async Task<Result> SetGlobalAsync(IEnumerable<Entity> entities, bool global)
    {
        var result = Result.Ok();
        foreach (var entity in entities.ToList())
        {
            result = await entity.SetGlobalAsync(global, CancellationToken.None).ContinueOnAnyContext();
            if (!result.Success)
                break;
        }

        await RefreshAsync().ContinueOnAnyContext();
        return result;
    }

    /// <summary>
    /// Opens a world asset (by handle) as THIS (active) world, replacing what the engine currently has loaded,
    /// then re-pulls so every surface reflects it. The engine preserves the current world on failure.
    /// </summary>
    public async Task<Result> OpenAsync(AssetHandle worldAsset, CancellationToken ct = default)
    {
        var result = await Engine
            .SendCommand(EngineMethods.WorldOpen, new { AssetId = worldAsset.Id }, ct).ContinueOnAnyContext();
        if (!result.Success)
            return result;

        ClearDirty();
        await RefreshAsync(ct).ContinueOnAnyContext();
        return result;
    }

    /// <summary>
    /// Re-fetches this world from the engine and republishes its entity tree through the reflected
    /// <see cref="Entities"/> field (the <see cref="EntityCollectionConverter"/> builds the typed tree). Failures
    /// surface as an empty world. The describe + tree build happen off the UI thread; <see cref="Changed"/>
    /// subscribers marshal. A world's "refresh" is this whole-tree re-pull, so it overrides the per-object
    /// reflected refresh.
    /// </summary>
    public override async Task<Result> RefreshAsync(CancellationToken ct = default)
    {
        var result = await Engine
            .SendCommand<JObject>(EngineMethods.WorldDescribe, new { WorldAssetId = AssetId }, ct).ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
        {
            Entities = [];
            return Result.Fail(result.Error ?? "The engine returned no world.");
        }

        // Hydrating the reflected entities field runs the converter (CPU-bound tree build) — offload it; setting
        // the field raises PropertyChanged, which attaches/indexes the tree and fires Changed (subscribers marshal).
        var body = new JObject { ["entities"] = reply["entities"] ?? new JArray() };
        await Task.Run(() => HydrateFromDescribe(body), ct).ContinueOnAnyContext();
        return Result.Ok();
    }

    /// <summary>Drops the entity tree and publishes an empty world (a disconnect / no world loaded).</summary>
    public void Clear()
    {
        ClearDirty();
        Entities = [];
    }

    /// <summary>Persists the live world back to its <c>.world</c>/<c>.chunk</c>/<c>.globals</c> files through
    /// the engine's <c>world.save</c> (not the body-based <c>asset.save</c>); clears dirty on success.</summary>
    public async Task<Result> SaveAsync(CancellationToken ct = default)
    {
        var result = await Engine.SendCommand(EngineMethods.WorldSave, null, ct).ContinueOnAnyContext();
        if (result.Success)
            ClearDirty();
        return result;
    }

    // The reflected Entities field just changed (hydrated by a refresh): attach + bind the fresh tree, rebuild the
    // id index, and publish it. Fired from PropertyChanged so any path that sets Entities keeps the tree consistent.
    private void OnSelfChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Entities))
            OnEntitiesChanged();
    }

    private void OnEntitiesChanged()
    {
        _byId.Clear();
        foreach (var root in Entities)
            root.Attach(this);
        Index(Entities);
        Changed?.Invoke(Entities);
    }

    private void Index(IReadOnlyList<Entity> nodes)
    {
        foreach (var node in nodes)
        {
            _byId[node.Id] = node;
            Index(node.Children);
        }
    }
}
