using System;
using System.Threading;
using System.Threading.Tasks;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Utils;
// Aliased: the simple name "World" would bind to this enclosing namespace, not the model type.
using WorldModel = Toybox.Studio.Models.Ecs.World;

namespace Toybox.Studio.Services.World;

/// <summary>
/// The editor-side world service: owns the current <see cref="Models.Ecs.World"/> snapshot, keeps it in sync
/// with the engine (re-fetched on connect), tracks whether it has unsaved editor changes, and saves it back to
/// the engine. The engine owns the authoritative data; this mirrors the engine's own WorldManager.
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

    public WorldManager(Session session, EngineRpc engine, JsonParser parser, Logger log)
    {
        _session = session;
        _engine = engine;
        _parser = parser;
        _log = log;
        session.StateChanged += OnSessionStateChanged;
    }

    /// <summary>The current world snapshot (empty until the first refresh / while disconnected).</summary>
    public WorldModel Current { get; private set; } = WorldModel.Empty;

    /// <summary>Raised with the new snapshot whenever the world changes.</summary>
    public event Action<WorldModel>? WorldChanged;

    /// <summary>Whether the world holds unsaved editor changes.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Raised whenever <see cref="IsDirty"/> changes.</summary>
    public event Action<bool>? DirtyChanged;

    /// <summary>
    /// Re-fetches the world from the engine. Failures surface as an empty world. All fetching and tree
    /// building happens off the UI thread.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var result = await _engine.DescribeWorldAsync(ct).ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
        {
            Publish(WorldModel.Empty);
            return;
        }

        var roots = await Task.Run(() => _parser.ParseWorld(reply), ct).ContinueOnAnyContext();
        Publish(new WorldModel(roots));
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
        var result = await _engine.SaveWorldAsync(ct).ContinueOnAnyContext();
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

    private void OnSessionStateChanged(ConnectionState state)
    {
        // A new session reloads the world from disk, so any prior dirty state is gone.
        SetDirty(false);
        if (state == ConnectionState.Connected)
            RefreshAsync().FireAndForget();
        else
            Publish(WorldModel.Empty);
    }

    private void Publish(WorldModel world)
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
