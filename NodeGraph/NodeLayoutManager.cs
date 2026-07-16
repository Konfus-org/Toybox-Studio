using Newtonsoft.Json;
using Toybox.Studio.Utils;

namespace Toybox.Studio.NodeGraph;

/// <summary>
/// The single owner of the per-project node layouts across every open viewport pane. Each world's layouts
/// are loaded from its <c>&lt;worldId&gt;.json</c> file the first time they're touched and mutated in memory
/// thereafter; a change persists just that world's file in the background, coalescing a burst of node drags
/// into the freshest write (a drag fires many updates a second). Panes over the same world share one map,
/// so a node moved in one pane is placed the same in another.
/// </summary>
public sealed class NodeLayoutManager
{
    private readonly NodeLayoutStore _store;

    // Guards the in-memory maps and the pending snapshots: reads/edits come from the UI thread while a
    // background flush clears its pending entry on a thread-pool thread, so every touch takes the lock.
    private readonly object _sync = new();

    // Each world's entity-id → layout map, loaded lazily on first access.
    private readonly Dictionary<ulong, Dictionary<ulong, NodeLayout>> _worlds = [];

    // One coalescing write gate + pending snapshot per world: only one write per world runs at a time and
    // the freshest snapshot wins (the SettingsManager pattern), so rapid drags collapse to the latest file.
    private readonly Dictionary<ulong, SemaphoreSlim> _gates = [];
    private readonly Dictionary<ulong, string> _pending = [];

    public NodeLayoutManager(NodeLayoutStore store) => _store = store;

    /// <summary>The layout for an entity's node in a world, creating (but not yet persisting) a collapsed
    /// default the first time — so reading a node never writes a file; only an actual edit does.</summary>
    public NodeLayout Layout(ulong worldId, ulong entityId)
    {
        lock (_sync)
        {
            var map = Map(worldId);
            if (!map.TryGetValue(entityId, out var layout))
                map[entityId] = layout = new NodeLayout();
            return layout;
        }
    }

    /// <summary>Persists a world's node layouts in the background (coalesced), dropping entries that match
    /// the default so the file stays to the user's actual deviations. Call after mutating a
    /// <see cref="NodeLayout"/> returned by <see cref="Layout"/>.</summary>
    public void Save(ulong worldId)
    {
        SemaphoreSlim gate;
        lock (_sync)
        {
            // Serialize under the lock for a race-free snapshot, keeping only non-default entries.
            var snapshot = Map(worldId)
                .Where(pair => !pair.Value.IsDefault)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            _pending[worldId] = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
            if (!_gates.TryGetValue(worldId, out gate!))
                _gates[worldId] = gate = new SemaphoreSlim(1, 1);
        }

        FlushAsync(worldId, gate).FireAndForget();
    }

    // The live map for a world, loaded from disk on first touch. Caller holds _sync.
    private Dictionary<ulong, NodeLayout> Map(ulong worldId)
    {
        if (!_worlds.TryGetValue(worldId, out var map))
            _worlds[worldId] = map = _store.Load(worldId);
        return map;
    }

    // Flushes the freshest pending snapshot for a world; a burst of Save calls collapses to the latest.
    private async Task FlushAsync(ulong worldId, SemaphoreSlim gate)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            string? pending;
            lock (_sync)
            {
                _pending.Remove(worldId, out pending);
            }

            if (pending is not null)
                await _store.SaveAsync(worldId, pending).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
