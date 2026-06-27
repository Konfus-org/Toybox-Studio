using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.World;

/// <summary>
/// The editor-side "World" construct: owns the current <see cref="WorldDescription"/>, keeps it in sync with the
/// engine (re-fetched on connect), tracks whether it has unsaved editor changes, saves it back, and is the
/// entry point for world-level mutations — creating entities and vending <see cref="Entity"/> handles for
/// per-entity operations. The engine owns the authoritative data; this mirrors the engine's own WorldManager.
///
/// Edits made while the game is playing don't dirty the world: the engine snapshots the world on Play and
/// restores it on Stop, so those changes hit a throwaway copy. A fresh engine session (Connected) reloads the
/// world from disk, so the snapshot refreshes and the dirty bit resets there.
/// </summary>
public sealed class WorldManager
{
    private readonly EngineRpc _engine;
    private readonly JsonParser _parser;
    private readonly Session _session;
    private readonly Logger _log;
    private readonly World _active;

    public WorldManager(Session session, EngineRpc engine, JsonParser parser, Logger log)
    {
        _session = session;
        _engine = engine;
        _parser = parser;
        _log = log;
        _active = new World(engine, parser, id: 0U, owner: this);
        session.StateChanged += OnSessionStateChanged;
        // The engine's transform gizmo edits entities directly; mirror that back into the editor.
        engine.TransformEdited += OnTransformEdited;
    }

    /// <summary>The active editing world (world id 0) — the root of the object graph the editor edits.</summary>
    public World Active => _active;

    /// <summary>
    /// A handle to an isolated asset-preview world by its engine id (returned from <c>view.start</c>),
    /// owned editor-side by <paramref name="owner"/> (e.g. the asset viewer). Per-entity ops on it resolve
    /// the world engine-side from the entity id; only its create/describe carry the id.
    /// </summary>
    public World ForPreview(uint worldId, object owner) => new(_engine, _parser, worldId, owner);

    /// <summary>Raised with the new snapshot whenever the world changes.</summary>
    public event Action<WorldDescription>? WorldChanged;

    /// <summary>Raised whenever <see cref="IsDirty"/> changes.</summary>
    public event Action<bool>? DirtyChanged;

    /// <summary>The current world snapshot (empty until the first refresh / while disconnected).</summary>
    public WorldDescription Current { get; private set; } = WorldDescription.Empty;

    /// <summary>Whether the world holds unsaved editor changes.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>A handle to one entity in the active world, by id, for per-entity operations (rename, move, …).</summary>
    public Entity Entity(ulong id) => _active.GetEntity(id);

    /// <summary>
    /// Creates a new entity in the active world (optionally named and parented; a zero/omitted parent means a
    /// root entity) and returns a handle to it. The engine appends it after its last sibling. The caller marks
    /// the world dirty and refreshes — this performs no implicit re-pull.
    /// </summary>
    public Task<Result<Entity>> CreateEntityAsync(string? name, ulong parent, CancellationToken ct) =>
        _active.CreateEntityAsync(name, parent, ct);

    /// <summary>
    /// Opens a world/chunk asset (by id) as the active editing world, replacing the current one, then
    /// re-pulls so the tree/inspector and every viewport reflect it. The engine preserves the current
    /// world on failure.
    /// </summary>
    public async Task<Result> OpenWorldAsync(long assetId, CancellationToken ct = default)
    {
        var result = await _engine
            .InvokeAsync("world.open", new { AssetId = assetId }, ct).ContinueOnAnyContext();
        if (!result.Success)
        {
            _log.Error(result.Error ?? "Failed to open the world.");
            return result;
        }

        // A freshly opened world starts clean; pull its entities so the editor reflects it.
        SetDirty(false);
        await RefreshAsync(ct).ContinueOnAnyContext();
        return result;
    }

    /// <summary>
    /// Re-fetches the world from the engine. Failures surface as an empty world. All fetching and tree
    /// building happens off the UI thread.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var result = await _engine
            .InvokeAsync<JObject>("world.describe", null, ct).ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
        {
            Publish(WorldDescription.Empty);
            return;
        }

        var roots = await Task.Run(() => _parser.ParseWorld(reply), ct).ContinueOnAnyContext();
        Publish(new WorldDescription(roots));
    }

    /// <summary>
    /// Marks the world dirty after an editor edit. No-ops while playing (that mutation hits the engine's
    /// throwaway play snapshot, which is discarded on Stop, so it is not a real change).
    /// </summary>
    public void MarkDirty()
    {
        if (_session.IsPlaying)
            return;
        SetDirty(true);
    }

    /// <summary>Saves the world back to the engine (its chunk + globals asset files); clears dirty on success.</summary>
    public async Task SaveAsync(CancellationToken ct = default)
    {
        var result = await _engine
            .InvokeAsync("world.save", null, ct).ContinueOnAnyContext();
        if (result.Success)
        {
            SetDirty(false);
            _log.Info("World saved.");
        }
        else
        {
            _log.Warning(result.Error ?? "Saving the world is not supported yet.");
        }
    }

    // The viewport gizmo moved one or more entities engine-side: mark the world dirty and re-pull so the
    // tree/inspector show the new transforms.
    private void OnTransformEdited()
    {
        MarkDirty();
        RefreshAsync().FireAndForget();
    }

    private void OnSessionStateChanged(ConnectionState state)
    {
        // A new session reloads the world from disk, so any prior dirty state is gone.
        SetDirty(false);
        if (state == ConnectionState.Connected)
            RefreshAsync().FireAndForget();
        else
            Publish(WorldDescription.Empty);
    }

    private void Publish(WorldDescription world)
    {
        Current = world;
        WorldChanged?.Invoke(world);
    }

    private void SetDirty(bool value)
    {
        if (IsDirty == value)
            return;
        IsDirty = value;
        DirtyChanged?.Invoke(value);
    }
}
