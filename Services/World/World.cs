using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.World.Components;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.World;

/// <summary>
/// A high-level handle to one engine world — the root of the editor's Unity-like object graph
/// (<c>World</c> → <see cref="Entity"/> → <see cref="Component"/>). It carries the engine world id and
/// an editor-side <see cref="Owner"/>, and fronts the world-level RPCs (create entity, describe). Per-
/// entity/component ops resolve their world from the entity id engine-side, so only world-level calls
/// carry the <see cref="Id"/>.
///
/// World id 0 is the active editing world (owned by the shell, vended as <see cref="WorldManager.Active"/>);
/// a non-zero id is an isolated asset-preview world (owned by an asset viewer, vended by
/// <see cref="WorldManager.ForPreview"/>). Cheap to create; holds no entity state.
/// </summary>
public sealed class World
{
    private readonly EngineRpc _engine;
    private readonly JsonParser _parser;

    internal World(EngineRpc engine, JsonParser parser, uint id, object? owner)
    {
        _engine = engine;
        _parser = parser;
        Id = id;
        Owner = owner;
    }

    /// <summary>The engine world id (0 = the active editing world; non-zero = an asset-preview world).</summary>
    public uint Id { get; }

    /// <summary>Who owns this world editor-side (the shell for the active world; an asset viewer for a
    /// preview). Lets callers tell a world's purpose without reaching for its id.</summary>
    public object? Owner { get; }

    // The shared transport + parser, exposed within the assembly so the entities/components this world
    // vends can reach the engine without each holding their own references.
    internal EngineRpc Engine => _engine;

    internal JsonParser Parser => _parser;

    /// <summary>A cheap identity handle to one entity in this world (no RPC).</summary>
    public Entity GetEntity(ulong id) => new(this, id);

    /// <summary>
    /// Creates a new entity in THIS world (optionally named and parented; a zero parent means a root
    /// entity) and returns a handle to it. The engine appends it after its last sibling. The caller
    /// refreshes — this performs no implicit re-pull.
    /// </summary>
    public async Task<Result<Entity>> CreateEntityAsync(string? name, ulong parent, CancellationToken ct)
    {
        var result = await _engine
            .InvokeAsync<JObject>("entity.create", new { Name = name ?? "", Parent = parent, WorldId = Id }, ct)
            .ContinueOnAnyContext();
        return result is { Success: true, Value: { } reply }
            ? Result<Entity>.Ok(GetEntity(reply.Value<ulong>("id")))
            : Result<Entity>.Fail(result.Error ?? "The engine returned no result.");
    }

    /// <summary>
    /// Snapshots this world's entity tree (its entities + components). Targets this world by its
    /// <see cref="Id"/>; an empty world surfaces as an empty description.
    /// </summary>
    public async Task<Result<WorldDescription>> DescribeAsync(CancellationToken ct = default)
    {
        var result = await _engine
            .InvokeAsync<JObject>("world.describe", new { WorldId = Id }, ct).ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
            return Result<WorldDescription>.Fail(result.Error ?? "The engine returned no world.");

        var roots = _parser.ParseWorld(reply);
        return Result<WorldDescription>.Ok(new WorldDescription(roots));
    }

    /// <summary>
    /// Recreates an entity in this world from a serialized entity body (the JSON
    /// <see cref="Entity.SerializeAsync"/> produces) — the engine of copy/paste and duplicate. A FRESH entity
    /// is created under <paramref name="parent"/> and each serialized component is re-applied; the script
    /// container is skipped (script bindings carry per-binding asset references with their own flow).
    /// </summary>
    public async Task<Result<Entity>> AddEntityFromJsonAsync(
        JObject entityJson, ulong parent, CancellationToken ct = default)
    {
        if (_parser.ParseEntity(entityJson) is not { } snapshot)
            return Result<Entity>.Fail("The entity JSON could not be parsed.");

        var created = await CreateEntityAsync(snapshot.Name, parent, ct).ContinueOnAnyContext();
        if (!created.Success || created.Value is not { } entity)
            return created;

        // setComponent adds-or-replaces, so a fresh entity that doesn't yet carry a component still takes it.
        foreach (var component in snapshot.Components)
            if (component.Name != "script_container")
                await entity.Component(component.Name).SetAsync(component.Raw, ct).ContinueOnAnyContext();

        if (snapshot.IsGlobal)
            await entity.SetGlobalAsync(true, ct).ContinueOnAnyContext();
        if (!snapshot.IsEnabled)
            await entity.SetEnabledAsync(false, ct).ContinueOnAnyContext();

        return Result<Entity>.Ok(entity);
    }

    /// <summary>
    /// The first entity in this world that carries the typed component <typeparamref name="T"/> (depth-first),
    /// or a failure when none does. Used to reach singletons like the sky entity in a preview world.
    /// </summary>
    public async Task<Result<Entity>> FindByComponentAsync<T>(CancellationToken ct = default)
        where T : IComponentType<T>
    {
        var described = await DescribeAsync(ct).ContinueOnAnyContext();
        if (described is not { Success: true, Value: { } data })
            return Result<Entity>.Fail(described.Error ?? "The engine returned no world.");

        return FindWithComponent(data.Roots, T.Wire) is { } id
            ? Result<Entity>.Ok(GetEntity(id))
            : Result<Entity>.Fail($"No entity with a '{T.Wire}' component.");
    }

    private static ulong? FindWithComponent(IReadOnlyList<EntityDescription> entities, string wire)
    {
        foreach (var entity in entities)
        {
            if (entity.Components.Any(component => component.Name == wire))
                return entity.Id;
            if (FindWithComponent(entity.Children, wire) is { } id)
                return id;
        }

        return null;
    }
}
