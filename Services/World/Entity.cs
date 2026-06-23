using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.World;

/// <summary>
/// The editor-side handle to one entity in the engine's world: a thin, identity-only construct (just an id +
/// the shared <see cref="EngineRpc"/> transport) that fronts the engine's per-entity RPCs so view-models call
/// <c>entity.RenameAsync(…)</c> instead of reaching for the wire. Cheap to create on demand — it holds no
/// state. Obtained from <see cref="WorldManager.Entity"/>; vends <see cref="Component"/> handles for its
/// components, and <see cref="DescribeAsync"/> returns the data counterpart
/// (<see cref="EntityDescription"/>) the tree/inspector bind to.
/// </summary>
public sealed class Entity
{
    private readonly EngineRpc _engine;
    private readonly JsonParser _parser;

    internal Entity(EngineRpc engine, JsonParser parser, ulong id)
    {
        _engine = engine;
        _parser = parser;
        Id = id;
    }

    /// <summary>The entity's stable engine id.</summary>
    public ulong Id { get; }

    /// <summary>A handle to one of this entity's components, by type name.</summary>
    public Component Component(string name) => new(_engine, Id, name);

    /// <summary>Renames the entity in place.</summary>
    public Task<Result> RenameAsync(string name, CancellationToken ct) =>
        _engine.InvokeAsync("entity.setName", new { EntityId = Id, Name = name }, ct);

    /// <summary>Promotes or demotes the entity between global (full-lifetime resident) and ordinary streamed.</summary>
    public Task<Result> SetGlobalAsync(bool global, CancellationToken ct) =>
        _engine.InvokeAsync("entity.setGlobal", new { EntityId = Id, Global = global }, ct);

    /// <summary>Toggles the entity's wholesale enable flag; disabled entities are skipped by every runtime
    /// system (rendering, physics, scripting) but stay listed in the editor.</summary>
    public Task<Result> SetEnabledAsync(bool enabled, CancellationToken ct) =>
        _engine.InvokeAsync("entity.setEnabled", new { EntityId = Id, Enabled = enabled }, ct);

    /// <summary>Destroys the entity and its whole subtree.</summary>
    public Task<Result> DestroyAsync(CancellationToken ct) =>
        _engine.InvokeAsync("entity.destroy", new { EntityId = Id }, ct);

    /// <summary>
    /// Moves the entity to <paramref name="parent"/> (zero = root) and to position <paramref name="index"/>
    /// among that parent's children — one call covers both reorder and reparent.
    /// </summary>
    public Task<Result> MoveAsync(ulong parent, int index, CancellationToken ct) =>
        _engine.InvokeAsync("entity.move", new { EntityId = Id, Parent = parent, Index = index }, ct);

    /// <summary>
    /// Re-fetches this entity's current reflected component data and parses it into a fresh
    /// <see cref="EntityDescription"/> (children are not resolved — the tree owns parenting). Used to keep the
    /// selected entity in sync with the running game without re-describing the whole world.
    /// </summary>
    public async Task<Result<EntityDescription>> DescribeAsync(CancellationToken ct)
    {
        var result = await _engine
            .InvokeAsync<Newtonsoft.Json.Linq.JObject>("entity.describe", new { EntityId = Id }, ct)
            .ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
            return Result<EntityDescription>.Fail(result.Error ?? "The engine returned no result.");

        return _parser.ParseEntity(reply) is { } snapshot
            ? Result<EntityDescription>.Ok(snapshot)
            : Result<EntityDescription>.Fail("The engine returned no entity.");
    }
}
