using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Hosting;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The engine's asset listing reply; the script-type catalog it also carries belongs to the
/// future scripting domain and is left unread.</summary>
internal sealed record AssetListing(IReadOnlyList<AssetEntry> Assets);

/// <summary>
/// The handle database: every asset the engine's registry knows, as <see cref="AssetEntry"/> rows. It
/// answers "what assets exist" and resolves ids and paths — loading and creating the typed mirrors is
/// the <see cref="AssetFactory"/>'s job. The listing refreshes itself as the engine connection comes
/// up (and empties when it drops); anything that changes the registry (a create, a future file watcher)
/// calls <see cref="RefreshAsync"/> to re-pull. Each swap dispatches one <see cref="AssetCatalogChanged"/>.
/// </summary>
public sealed class AssetCatalog : EventSubscriber, IEventHandler<ConnectionChanged>
{
    private readonly Engine _engine;
    private readonly Logger _log;

    public AssetCatalog(Engine engine, Logger log, EventDispatcher events) : base(events)
    {
        _engine = engine;
        _log = log;
    }

    /// <summary>The current listing, newest refresh wins; empty while no engine is connected.</summary>
    public IReadOnlyList<AssetEntry> Entries { get; private set; } = [];

    public void Handle(in ConnectionChanged evt)
    {
        if (evt.State == ConnectionState.Connected)
            RefreshInternalAsync().FireAndForget();
        else if (evt.State == ConnectionState.Disconnected)
            Clear();
    }

    /// <summary>Re-pulls the listing from the engine and swaps it in (one change notification).</summary>
    public async Task<Result> RefreshAsync(CancellationToken ct = default)
    {
        var reply = await _engine
            .SendCommandAsync<AssetListing>(EngineCommands.EditorListAssets, null, ct)
            .ContinueOnAnyContext();
        if (!reply)
            return Result.Fail(reply.Error!);

        Entries = reply.Value?.Assets ?? [];
        Events.Dispatch(new AssetCatalogChanged(Entries));
        return Result.Ok();
    }

    /// <summary>The entry with the given id, or null when the catalog doesn't know it.</summary>
    public AssetEntry? Find(ulong id) => Entries.FirstOrDefault(entry => entry.Id == id);

    /// <summary>The entry at the given engine-normalized project-relative path, or null.</summary>
    public AssetEntry? Find(string path) => Entries.FirstOrDefault(
        entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase));

    private async Task RefreshInternalAsync()
    {
        var refreshed = await RefreshAsync().ContinueOnAnyContext();
        if (!refreshed)
            _log.Warning($"The asset catalog could not refresh: {refreshed.Error}");
    }

    private void Clear()
    {
        if (Entries.Count == 0)
            return;

        Entries = [];
        Events.Dispatch(new AssetCatalogChanged(Entries));
    }
}
