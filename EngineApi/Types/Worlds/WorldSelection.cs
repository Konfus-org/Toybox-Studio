using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types.Components;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Hosting;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi.Types.Worlds;

/// <summary>
/// The editor's entity selection — the one source of truth every panel observes (world tree, inspector,
/// viewport picking, gizmo anchoring). An ordered set of entity ids, last-added = primary: the entity
/// the inspector shows and local-space gizmos anchor to. Ids rather than <see cref="Entity"/> refs, so
/// the selection survives a world refresh that rebuilds entity snapshots. Mutate through the methods
/// below — each no-ops when nothing would change, so listeners and the wire only ever see real edits.
/// Engine-global state (no address), synced Batched so marquee-drag churn coalesces; the engine's
/// selection is process state, so this re-pushes itself whenever the connection comes (back) up. One
/// instance, created, bound, and disposed by the composition root.
/// </summary>
[EngineSync(EngineCommands.SelectionSet, SyncMode.Batched)]
public sealed partial class WorldSelection : IEventHandler<ConnectionChanged>
{
    private readonly EventDispatcher _events;
    private readonly Logger _log;

    public WorldSelection(EventDispatcher events, Logger log)
    {
        _events = events;
        _log = log;
        SelectedIds = [];
        events.RegisterAll(this);
    }

    /// <summary>The selected entity ids, in selection order — the last one is the primary.</summary>
    [EngineSync(Converter = typeof(EntityIdListConverter), Key = "ids")]
    public partial IReadOnlyList<ulong> SelectedIds { get; private set; }

    /// <summary>The primary (active) entity — the last id selected — or null when nothing is.</summary>
    public ulong? PrimaryId => SelectedIds is { Count: > 0 } ids ? ids[^1] : null;

    /// <summary>Whether a bulk rebuild (a tree reconcile) is underway. <c>Changed</c> still fires —
    /// this flag lets selection adapters tell structural churn from user edits.</summary>
    public bool IsBatching { get; private set; }

    /// <summary>Whether the entity is in the selection.</summary>
    public bool Contains(ulong id) => SelectedIds.Contains(id);

    /// <summary>The plain-click gesture: select exactly this entity, or clear on null (a miss).</summary>
    public void Select(ulong? id)
    {
        if (id is { } value)
            Set(value);
        else
            Clear();
    }

    /// <summary>Replaces the selection with exactly this entity.</summary>
    public void Set(ulong id)
    {
        if (SelectedIds is [var only] && only == id)
            return;

        SelectedIds = [id];
    }

    /// <summary>Adds the entity to the selection and makes it the primary (moves it to the end when
    /// already selected).</summary>
    public void Add(ulong id)
    {
        if (PrimaryId == id)
            return;

        SelectedIds = [.. SelectedIds.Where(existing => existing != id), id];
    }

    /// <summary>The ctrl-click gesture: removes a selected entity, or adds (and makes primary) an
    /// unselected one.</summary>
    public void Toggle(ulong id)
    {
        if (Contains(id))
            Remove(id);
        else
            SelectedIds = [.. SelectedIds, id];
    }

    /// <summary>Replaces the selection with these ids in order — the marquee/shift-range gesture; the
    /// last id becomes the primary. Duplicates collapse to their first occurrence.</summary>
    public void SetMany(IReadOnlyList<ulong> ids)
    {
        ulong[] incoming = [.. ids.Distinct()];
        if (SelectedIds.SequenceEqual(incoming))
            return;

        SelectedIds = incoming;
    }

    /// <summary>Removes one entity from the selection, keeping the rest — the pruning hook for an
    /// entity that no longer exists.</summary>
    public void Remove(ulong id)
    {
        if (!Contains(id))
            return;

        SelectedIds = [.. SelectedIds.Where(existing => existing != id)];
    }

    /// <summary>Empties the selection — the empty-space click / Escape gesture.</summary>
    public void Clear()
    {
        if (SelectedIds.Count == 0)
            return;

        SelectedIds = [];
    }

    /// <summary>Marks a bulk rebuild as underway (see <see cref="IsBatching"/>).</summary>
    public void BeginBatch() => IsBatching = true;

    /// <summary>Marks the bulk rebuild as finished.</summary>
    public void EndBatch() => IsBatching = false;

    public void Handle(in ConnectionChanged evt)
    {
        if (evt.State != ConnectionState.Connected)
            return;

        RepushAsync().FireAndForget();
    }

    public override void Dispose()
    {
        _events.UnregisterAll(this);
        base.Dispose();
    }

    // The selection's synced body, resent value by value — the same {key, value} payloads its property
    // pushes send, so the engine handler sees one shape regardless of how a value arrives. An empty
    // selection has nothing to restore — a (re)launched engine already starts with none selected.
    private async Task RepushAsync()
    {
        if (SelectedIds.Count == 0)
            return;

        foreach (var (key, value) in Serialize())
        {
            if (value is null)
                continue;

            var payload = new JObject
            {
                ["key"] = key,
                ["value"] = value,
            };
            var result = await SendCommandAsync(EngineCommands.SelectionSet, payload, CancellationToken.None)
                .ContinueOnAnyContext();
            if (!result)
            {
                _log.Warning($"Re-pushing the world selection failed: {result.Error}");
                return;
            }
        }
    }
}
