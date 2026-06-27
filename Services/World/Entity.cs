using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.World.Components;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.World;

/// <summary>
/// A handle to one entity in a <see cref="World"/>: it knows the <see cref="World"/> it belongs to and its
/// <see cref="Id"/>, and fronts the engine's per-entity RPCs so view-models call <c>entity.RenameAsync(…)</c>
/// instead of reaching for the wire. Cheap to create on demand; per-entity ops resolve the owning world
/// engine-side from the id, so they never carry the world id.
///
/// Naturally-scalar state is exposed as optimistic <see cref="Name"/>/<see cref="Enabled"/>/
/// <see cref="IsGlobal"/> properties (set pushes to the engine; the owning document re-syncs on rejection);
/// the <c>*Async</c> methods are kept for callers that need the <see cref="Result"/>. Obtained from
/// <see cref="World.GetEntity"/>; vends <see cref="Component"/> handles for its components.
/// </summary>
public sealed class Entity
{
    private readonly World _world;

    // Optimistic cached scalars; seeded when the handle is created from a snapshot, else defaulted. The
    // property getters read these; the setters push to the engine and update the cache.
    private string _name;
    private bool _enabled;
    private bool _isGlobal;

    internal Entity(World world, ulong id, string name = "", bool enabled = true, bool isGlobal = false)
    {
        _world = world;
        Id = id;
        _name = name;
        _enabled = enabled;
        _isGlobal = isGlobal;
    }

    /// <summary>The entity's stable engine id.</summary>
    public ulong Id { get; }

    /// <summary>The world this entity belongs to.</summary>
    public World World => _world;

    private EngineRpc Engine => _world.Engine;

    private JsonParser Parser => _world.Parser;

    /// <summary>The entity's name. Setting it pushes optimistically (the owning document re-syncs on
    /// rejection); use <see cref="RenameAsync"/> when you need the <see cref="Result"/>.</summary>
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            RenameAsync(value, CancellationToken.None).FireAndForget();
        }
    }

    /// <summary>Whether the entity is enabled. Optimistic setter (see <see cref="Name"/>).</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            SetEnabledAsync(value, CancellationToken.None).FireAndForget();
        }
    }

    /// <summary>Whether the entity is global (a full-lifetime resident). Optimistic setter (see
    /// <see cref="Name"/>).</summary>
    public bool IsGlobal
    {
        get => _isGlobal;
        set
        {
            _isGlobal = value;
            SetGlobalAsync(value, CancellationToken.None).FireAndForget();
        }
    }

    /// <summary>A handle to one of this entity's components, by type name (the dynamic/schema-driven path).</summary>
    public Component Component(string name) => new(this, name);

    // --- Typed, string-free component access (the curated marker types). ---

    /// <summary>Adds the typed component at its defaults; fails if unknown or already present.</summary>
    public Task<Result> AddComponentAsync<T>(CancellationToken ct = default)
        where T : IComponentType<T> =>
        AddComponentAsync(T.Wire, ct);

    /// <summary>Removes the typed component from this entity.</summary>
    public Task<Result> RemoveComponentAsync<T>(CancellationToken ct = default)
        where T : IComponentType<T> =>
        Component(T.Wire).RemoveAsync(ct);

    /// <summary>Replaces the typed component's whole state (one <c>entity.setComponent</c> round-trip).</summary>
    public Task<Result> SetComponentAsync<T>(T component, CancellationToken ct = default)
        where T : IComponentType<T> =>
        Component(T.Wire).SetAsync(component.ToComponentJson(), ct);

    /// <summary>Whether this entity currently carries the typed component.</summary>
    public async Task<Result<bool>> HasComponentAsync<T>(CancellationToken ct = default)
        where T : IComponentType<T>
    {
        var described = await DescribeAsync(ct).ContinueOnAnyContext();
        return described is { Success: true, Value: { } data }
            ? Result<bool>.Ok(data.Components.Any(component => component.Name == T.Wire))
            : Result<bool>.Fail(described.Error ?? "The engine returned no entity.");
    }

    /// <summary>Reads the typed component's current values, or fails when it isn't present.</summary>
    public async Task<Result<T>> GetComponentAsync<T>(CancellationToken ct = default)
        where T : IComponentType<T>
    {
        var described = await DescribeAsync(ct).ContinueOnAnyContext();
        if (described is not { Success: true, Value: { } data })
            return Result<T>.Fail(described.Error ?? "The engine returned no entity.");

        var component = data.Components.FirstOrDefault(candidate => candidate.Name == T.Wire);
        return component is null
            ? Result<T>.Fail($"Entity has no '{T.Wire}' component.")
            : Result<T>.Ok(T.FromComponentJson(component.Raw));
    }

    /// <summary>Adds the named component to this entity at its default values; fails if it is unknown or
    /// already present.</summary>
    public Task<Result> AddComponentAsync(string component, CancellationToken ct) =>
        Engine.InvokeAsync("entity.addComponent", new { EntityId = Id, Component = component }, ct);

    /// <summary>Attaches the given script asset to this entity (creating its script container if needed),
    /// appending a binding that runs the script at its source defaults.</summary>
    public Task<Result> AddScriptAsync(long script, CancellationToken ct) =>
        Engine.InvokeAsync("entity.addScript", new { EntityId = Id, Script = script }, ct);

    /// <summary>Renames the entity in place.</summary>
    public Task<Result> RenameAsync(string name, CancellationToken ct) =>
        Engine.InvokeAsync("entity.setName", new { EntityId = Id, Name = name }, ct);

    /// <summary>Promotes or demotes the entity between global (full-lifetime resident) and ordinary streamed.</summary>
    public Task<Result> SetGlobalAsync(bool global, CancellationToken ct) =>
        Engine.InvokeAsync("entity.setGlobal", new { EntityId = Id, Global = global }, ct);

    /// <summary>Toggles the entity's wholesale enable flag; disabled entities are skipped by every runtime
    /// system (rendering, physics, scripting) but stay listed in the editor.</summary>
    public Task<Result> SetEnabledAsync(bool enabled, CancellationToken ct) =>
        Engine.InvokeAsync("entity.setEnabled", new { EntityId = Id, Enabled = enabled }, ct);

    /// <summary>Destroys the entity and its whole subtree.</summary>
    public Task<Result> DestroyAsync(CancellationToken ct) =>
        Engine.InvokeAsync("entity.destroy", new { EntityId = Id }, ct);

    /// <summary>
    /// Moves the entity to <paramref name="parent"/> (zero = root) and to position <paramref name="index"/>
    /// among that parent's children — one call covers both reorder and reparent.
    /// </summary>
    public Task<Result> MoveAsync(ulong parent, int index, CancellationToken ct) =>
        Engine.InvokeAsync("entity.move", new { EntityId = Id, Parent = parent, Index = index }, ct);

    /// <summary>
    /// The entity's whole serialized state as JSON (its components + identity), for copy/paste and any
    /// "give me the JSON" flow. Round-trips through <see cref="World.AddEntityFromJsonAsync"/>.
    /// </summary>
    public async Task<Result<JObject>> SerializeAsync(CancellationToken ct = default)
    {
        var result = await Engine
            .InvokeAsync<JObject>("entity.describe", new { EntityId = Id }, ct).ContinueOnAnyContext();
        return result is { Success: true, Value: { } reply }
            ? Result<JObject>.Ok(reply)
            : Result<JObject>.Fail(result.Error ?? "The engine returned no entity.");
    }

    /// <summary>
    /// Re-fetches this entity's current reflected component data and parses it into a fresh
    /// <see cref="EntityDescription"/> (children are not resolved — the tree owns parenting). Used to keep the
    /// selected entity in sync with the running game without re-describing the whole world.
    /// </summary>
    public async Task<Result<EntityDescription>> DescribeAsync(CancellationToken ct)
    {
        var result = await Engine
            .InvokeAsync<JObject>("entity.describe", new { EntityId = Id }, ct)
            .ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
            return Result<EntityDescription>.Fail(result.Error ?? "The engine returned no result.");

        return Parser.ParseEntity(reply) is { } snapshot
            ? Result<EntityDescription>.Ok(snapshot)
            : Result<EntityDescription>.Fail("The engine returned no entity.");
    }
}
