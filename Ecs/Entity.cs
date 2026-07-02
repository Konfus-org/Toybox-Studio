using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Ecs.Components;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Extensions;
using Toybox.Studio.Worlds;

namespace Toybox.Studio.Ecs;

/// <summary>
/// One entity in a <see cref="World"/> — the single type for both the live snapshot data (name, tags, order,
/// global/enabled state, children, components) and the behaviour that mutates it. It reads like the runtime:
/// <c>entity.DeleteAsync()</c>, <c>entity.Name = "Foo"</c>, <c>entity.Order = 3</c>. The snapshot is rebuilt on
/// every world refresh (the entity converter hydrates a fresh detached tree; the world attaches + binds it); the
/// persistent observable layer the UI binds to reconciles against it by <see cref="Id"/>.
///
/// Its scalar state and its <see cref="Components"/> collection are all reflected <c>[EngineSync]</c> fields — the
/// generated setters push optimistically to the engine through the entity's <see cref="EngineAddress"/> (each a
/// uniform <c>reflect.set</c>; the owning document re-syncs on rejection), and the <c>*Async</c> verbs are kept
/// for callers that need the <see cref="Result"/>. Reordering is just setting <see cref="Order"/>; the view-model
/// owns the "move up/down among siblings" logic. The high-level behaviours
/// (<see cref="DeleteAsync"/>/<see cref="DuplicateAsync"/>/<see cref="MakeGlobalAsync"/>) coordinate the world's
/// refresh through the owning <see cref="World"/>; they return a <see cref="Result"/> and never raise UI.
///
/// A bare handle (<see cref="World.GetEntity"/>) carries only its id and empty children/components — enough to
/// issue per-entity RPCs.
/// </summary>
public sealed partial class Entity : EngineSyncedObject
{
    private World _world = null!;
    private readonly List<Entity> _children = [];

    // The reflected scalar state — the generator emits the public Name/IsEnabled/IsGlobal/Order/Tags properties
    // whose setters push through the bound entity address. Hydration seeds these fields under suppression (no push).
    [EngineSync] private string _name = string.Empty;
    [EngineSync] private bool _isEnabled = true;
    [EngineSync] private bool _isGlobal;

    // Explicit sibling order; entities sharing a parent+bucket sort by this (then id). Reflected, so reordering is
    // a plain Order set — the view-model's move-up/down computes the new value.
    [EngineSync] private int _order;

    // The entity's serialized gameplay tags (UE-style, hierarchical dot-names). Reflected as the bare string
    // array the engine's tags wire uses. Transient runtime tags (e.g. editor.selected) never appear here.
    [EngineSync] private IReadOnlyList<string> _tags = [];

    // The entity's components, reflected via a converter that mints the typed Component subclass per wire (or an
    // UnknownComponent) from the describe map. Hydrate-only: the collection isn't pushed wholesale — live edits go
    // per-field, structural changes through addComponent/removeComponent.
    [EngineSync(converter: typeof(ComponentCollectionConverter), mode: EngineSyncMode.Hydrate)]
    private IReadOnlyList<Component> _components = [];

    /// <summary>The entity's stable engine id.</summary>
    public ulong Id { get; }

    /// <summary>The world this entity belongs to.</summary>
    public World World => _world;

    /// <summary>A string-free reference to this entity (id + name), held where a node would be overkill and
    /// resolved back via <see cref="World.Find(EntityHandle)"/>.</summary>
    public EntityHandle Handle => new(Id, Name);

    /// <summary>The id of this entity's parent in the engine hierarchy (0 = a root). Set by the converter.</summary>
    public ulong ParentId { get; private set; }

    /// <summary>This entity's children in the engine hierarchy (both streamed and global; the UI splits them
    /// into its two forests). Assembled by the converter.</summary>
    public IReadOnlyList<Entity> Children => _children;

    /// <summary>Constructs a detached entity (no world yet) — the form the entity converter builds; the owning
    /// <see cref="World"/> then <see cref="Attach"/>es it, binding it and its components for live edits.</summary>
    internal Entity(ulong id) => Id = id;

    /// <summary>Destroys this entity and its subtree, then re-syncs the world. Batches through the world so a
    /// multi-entity caller pays one refresh.</summary>
    public Task<Result> DeleteAsync() => _world.DeleteAsync([this]);

    /// <summary>Duplicates this entity (a fresh-id copy under the same parent), then re-syncs the world.</summary>
    public Task<Result> DuplicateAsync() => _world.DuplicateAsync([this]);

    /// <summary>Promotes this entity to global (<paramref name="global"/> true) or demotes it to streamed.</summary>
    public Task<Result> MakeGlobalAsync(bool global) => _world.SetGlobalAsync([this], global);

    /// <summary>A bare handle to one of this entity's components, by wire name (the dynamic/schema-driven path) —
    /// identity only, for one-shot ops; the full typed instances live on <see cref="Components"/>.</summary>
    public Component GetComponent(string name)
    {
        var component = new UnknownComponent { Name = name };
        component.Attach(Id, _world);
        return component;
    }

    /// <summary>
    /// The typed component <typeparamref name="T"/> on this entity, re-fetched from the engine and bound so its
    /// property setters push live. The converter mints the typed instance for each wire (see
    /// <see cref="Components"/>), so this re-describes and returns it. Fails when the component isn't present.
    /// </summary>
    public async Task<Result<T>> GetComponentAsync<T>(CancellationToken ct = default)
        where T : Component, new()
    {
        var wire = typeof(T).Name.ToSnakeCase();
        var described = await DescribeAsync(ct).ContinueOnAnyContext();
        if (described is not { Success: true, Value: { } data })
            return Result<T>.Fail(described.Error ?? "The engine returned no entity.");

        return data.Components.FirstOrDefault(candidate => candidate.Name == wire) is T typed
            ? Result<T>.Ok(typed)
            : Result<T>.Fail($"Entity has no '{wire}' component.");
    }

    /// <summary>Adds the typed component at its engine defaults; fails if unknown or already present.</summary>
    public Task<Result> AddComponentAsync<T>(CancellationToken ct = default)
        where T : Component =>
        AddComponentAsync(typeof(T).Name.ToSnakeCase(), ct);

    /// <summary>Removes the typed component from this entity.</summary>
    public Task<Result> RemoveComponentAsync<T>(CancellationToken ct = default)
        where T : Component =>
        GetComponent(typeof(T).Name.ToSnakeCase()).RemoveAsync(ct);

    /// <summary>Whether this entity currently carries the typed component.</summary>
    public async Task<Result<bool>> HasComponentAsync<T>(CancellationToken ct = default)
        where T : Component
    {
        var wire = typeof(T).Name.ToSnakeCase();
        var described = await DescribeAsync(ct).ContinueOnAnyContext();
        return described is { Success: true, Value: { } data }
            ? Result<bool>.Ok(data.Components.Any(component => component.Name == wire))
            : Result<bool>.Fail(described.Error ?? "The engine returned no entity.");
    }

    /// <summary>Adds the named component to this entity at its default values; fails if it is unknown or
    /// already present.</summary>
    public Task<Result> AddComponentAsync(string component, CancellationToken ct) =>
        Structural(Engine.SendCommand(EngineMethods.EntityAddComponent, Args(("component", component)), ct));

    /// <summary>Attaches the given script asset to this entity (creating its script container if needed),
    /// appending a binding that runs the script at its source defaults.</summary>
    public Task<Result> AddScriptAsync(ulong script, CancellationToken ct) =>
        Structural(Engine.SendCommand(EngineMethods.EntityAddScript, Args(("script", script)), ct));

    /// <summary>Renames the entity in place (the awaited form of setting <see cref="Name"/>).</summary>
    public Task<Result> RenameAsync(string name, CancellationToken ct) =>
        SetWireAsync("name", name, ct);

    /// <summary>Promotes or demotes the entity between global (full-lifetime resident) and ordinary streamed.</summary>
    public Task<Result> SetGlobalAsync(bool global, CancellationToken ct) =>
        SetWireAsync("is_global", global, ct);

    /// <summary>Toggles the entity's wholesale enable flag; disabled entities are skipped by every runtime
    /// system (rendering, physics, scripting) but stay listed in the editor.</summary>
    public Task<Result> SetEnabledAsync(bool enabled, CancellationToken ct) =>
        SetWireAsync("is_enabled", enabled, ct);

    /// <summary>Destroys the entity and its whole subtree.</summary>
    public Task<Result> DestroyAsync(CancellationToken ct) =>
        Structural(Engine.SendCommand(EngineMethods.EntityDestroy, Args(), ct));

    /// <summary>
    /// Moves the entity to <paramref name="parent"/> (zero = root) and to position <paramref name="index"/>
    /// among that parent's children — one call covers both reorder and reparent (the world tree's drag-and-drop).
    /// </summary>
    public Task<Result> MoveAsync(ulong parent, int index, CancellationToken ct) =>
        Structural(Engine.SendCommand(EngineMethods.EntityMove, Args(("parent", parent), ("index", index)), ct));

    /// <summary>
    /// Applies a serialized entity body (from <see cref="EngineSyncedObject.Serialize"/>) onto this entity: pushes
    /// its name, then adds/replaces each component (the script container is skipped), then its global/enabled
    /// state. The restore half of copy/paste + duplicate — the caller creates a fresh entity, then deserializes
    /// into it. Overrides the base field-seed with this structural apply.
    /// </summary>
    public override async Task<Result> Deserialize(JObject body, CancellationToken ct = default)
    {
        if (Unwrap(body["name"])?.Value<string>() is { Length: > 0 } name)
        {
            var result = await SetWireAsync("name", name, ct).ContinueOnAnyContext();
            if (!result.Success)
                return result;
        }

        // setComponent adds-or-replaces, so a fresh entity that doesn't yet carry a component still takes it.
        if (Unwrap(body["components"]) is JObject components)
            foreach (var property in components.Properties())
                if (property.Name != "script_container" && property.Value is JObject componentBody)
                {
                    var result = await GetComponent(property.Name).Deserialize(componentBody, ct)
                        .ContinueOnAnyContext();
                    if (!result.Success)
                        return result;
                }

        if (Unwrap(body["is_global"])?.Value<bool?>() == true)
            await SetGlobalAsync(true, ct).ContinueOnAnyContext();
        if (Unwrap(body["is_enabled"])?.Value<bool?>() == false)
            await SetEnabledAsync(false, ct).ContinueOnAnyContext();

        return Result.Ok();
    }

    /// <summary>
    /// Re-fetches this entity's current reflected component data and parses it into a fresh, standalone
    /// <see cref="Entity"/> (children are not resolved — the tree owns parenting), bound to this world. Used to
    /// keep the selected entity in sync with the running game without re-describing the whole world.
    /// </summary>
    public async Task<Result<Entity>> DescribeAsync(CancellationToken ct)
    {
        var result = await Engine
            .SendCommand<JObject>(EngineMethods.EntityDescribe, Args(), ct)
            .ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
            return Result<Entity>.Fail(result.Error ?? "The engine returned no result.");
        if (reply["entity"] is not JObject element)
            return Result<Entity>.Fail("The engine returned no entity.");

        var entity = EntityCollectionConverter.BuildEntity(element);
        entity.Attach(_world);
        return Result<Entity>.Ok(entity);
    }

    /// <summary>Attaches this (converter-built, detached) entity and its subtree to <paramref name="world"/>,
    /// binding each entity + component to its reflect address so edits push. Called by the world once the tree is
    /// hydrated. Idempotent enough to re-run on a re-pull (Bind just re-points the address).</summary>
    internal void Attach(World world)
    {
        _world = world;
        Bind(EngineAddress.World(world.AssetId).Entity(Id), world.Scheduler);
        foreach (var component in _components)
            component.Attach(Id, world);
        foreach (var child in _children)
            child.Attach(world);
    }

    // Sets the parent id read from the describe element (not a reflected field — the engine reply is flat and the
    // converter assembles parenting).
    internal void SetParent(ulong parentId) => ParentId = parentId;

    // Reorders the (already-hydrated) components to the engine's registration order (component_order), a sibling
    // of the components map the field converter can't see; a no-op with no order list. The inspector still pins
    // transform first on top of this.
    internal void OrderComponents(JArray? order)
    {
        if (order is not { Count: > 0 } || _components.Count == 0)
            return;

        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < order.Count; index++)
            rank[order[index].Value<string>() ?? string.Empty] = index;

        _components = _components
            .OrderBy(component => rank.TryGetValue(component.Name, out var index) ? index : int.MaxValue)
            .ToList();
    }

    internal void AddChild(Entity child) => _children.Add(child);


    // A field edit dirties (and stamps) the owning world, which owns this entity tree.
    protected override void OnFieldEdited(string wire) => _world?.NotifyEdited();

    private Engine Engine => _world.Engine;

    // Runs a structural op, dirtying the owning world on success (structural edits don't flow through the
    // reflected field path, so they signal here).
    private async Task<Result> Structural(Task<Result> op)
    {
        var result = await op.ContinueOnAnyContext();
        if (result.Success)
            _world.NotifyEdited();
        return result;
    }

    // Every per-entity RPC carries the same addressing envelope — this entity's id plus its world's id, so the
    // engine resolves the right world when entity ids collide between the active world and a preview world
    // (0 = the active world). The keys are the literal camelCase wire names the engine reads.
    private Dictionary<string, object?> Args(params (string Key, object? Value)[] extra)
    {
        var args = new Dictionary<string, object?>
        {
            ["entityId"] = Id,
            ["worldAssetId"] = _world.AssetId,
        };
        foreach (var (key, value) in extra)
            args[key] = value;
        return args;
    }

    // The bare value of an entity-envelope field: both the attributed ({attributes,value}) and lean ({type,value})
    // shapes put the value under the top-level "value" key, so a plain lookup covers both.
    private static JToken? Unwrap(JToken? token) =>
        token is JObject obj && obj["value"] is { } value ? value : token;
}
