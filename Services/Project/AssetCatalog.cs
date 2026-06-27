using Toybox.Studio.Utils;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;
namespace Toybox.Studio.Services.Project;

/// <summary>
/// One registered script type: its class name and serialization version.
/// </summary>
public sealed record ScriptEntry(string Name, int Version);

/// <summary>
/// The engine's reply to editor.listAssets.
/// </summary>
public sealed record AssetCatalogReply(List<AssetInfo> Assets, List<ScriptEntry> Scripts);

/// <summary>
/// Keeps a UI-ready catalog of the engine's assets and scripts, refreshed on connect, so the property
/// grid can resolve handle/script ids to names and populate asset pickers. Mirrors the
/// <see cref="Toybox.Studio.Services.World.WorldManager"/>'s describe-on-connect pattern.
/// </summary>
public sealed class AssetCatalog : IListenable
{
    private readonly EngineRpc _engine;
    private Dictionary<long, AssetInfo> _byId = [];

    // Bumped on every connection-state change. A refresh captures it before its RPC and drops the result if
    // the generation moved on (a disconnect or a newer refresh) so a slow reply can't publish over a newer
    // (e.g. empty, post-disconnect) state. All catalog state is published on the UI thread, so the fields are
    // only ever written there and reads from the UI stay consistent.
    private int _generation;

    public AssetCatalog(Session session, EngineRpc engine)
    {
        _engine = engine;
        session.StateChanged += OnSessionStateChanged;
    }

    /// <summary>
    /// Raised on the thread pool after the catalog is refreshed; widgets marshal to the UI thread.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Raised when a script/asset link is activated (clicked). No-op until a host subscribes.
    /// </summary>
    public event Action<long>? AssetActivated;

    public IReadOnlyList<AssetInfo> Assets { get; private set; } = [];

    public IReadOnlyList<ScriptEntry> Scripts { get; private set; } = [];

    /// <summary>
    /// Assets matching any of the given type tokens (case-insensitive); all assets when none are given.
    /// </summary>
    public IReadOnlyList<AssetInfo> AssetsOfType(IReadOnlyList<string>? types)
    {
        if (types is not { Count: > 0 })
            return Assets;

        var wanted = new HashSet<string>(types, StringComparer.OrdinalIgnoreCase);
        return Assets.Where(asset => wanted.Contains(asset.Type)).ToList();
    }

    public AssetInfo? Resolve(long id) => _byId.GetValueOrDefault(id);

    public string? ResolveName(long id) => Resolve(id)?.Name;

    /// <summary>A string-free <see cref="AssetHandle"/> for an id (carrying its name/type/path), or
    /// <see cref="AssetHandle.None"/> when the id is unknown.</summary>
    public AssetHandle Handle(long id) =>
        Resolve(id) is { } asset ? new AssetHandle(asset.Id, asset.Name, asset.Type, asset.Path) : AssetHandle.None;

    /// <summary>Resolves an asset by its project-relative path or display name to an
    /// <see cref="AssetHandle"/> (<see cref="AssetHandle.None"/> when nothing matches). Path wins over name.</summary>
    public AssetHandle Find(string nameOrPath)
    {
        var match = Assets.FirstOrDefault(asset =>
                        string.Equals(asset.Path, nameOrPath, StringComparison.OrdinalIgnoreCase))
                    ?? Assets.FirstOrDefault(asset =>
                        string.Equals(asset.Name, nameOrPath, StringComparison.OrdinalIgnoreCase));
        return match is null ? AssetHandle.None : new AssetHandle(match.Id, match.Name, match.Type, match.Path);
    }

    public void Activate(long id) => AssetActivated?.Invoke(id);

    /// <summary>
    /// Loads an asset's editable body (an <c>asset.describe</c> snapshot) into an <see cref="Asset"/> for
    /// modify + save. Fails when the handle is unknown or the engine can't describe it (only materials are
    /// describable today).
    /// </summary>
    public async Task<Result<Asset>> LoadAsync(AssetHandle handle, CancellationToken ct = default)
    {
        if (handle.IsNone || Resolve(handle.Id) is not { } info)
            return Result<Asset>.Fail("Unknown asset.");

        var result = await _engine
            .InvokeAsync<JObject>("asset.describe", new { AssetId = handle.Id }, ct).ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
            return Result<Asset>.Fail(result.Error ?? "The engine returned no asset.");

        // The describe body lives under the asset-type key (e.g. "material" for a .mat).
        var body = reply["material"] as JObject ?? reply;
        return Result<Asset>.Ok(new Asset(_engine, info, body));
    }

    /// <summary>Loads an asset by project-relative path or display name (see <see cref="Find"/>).</summary>
    public Task<Result<Asset>> LoadAsync(string nameOrPath, CancellationToken ct = default) =>
        LoadAsync(Find(nameOrPath), ct);

    /// <summary>
    /// Re-fetches the catalog from the engine. Failures surface as an empty catalog.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        // Capture the generation this refresh belongs to; if the connection changes (disconnect, or a newer
        // refresh) before the reply lands, the stale result is dropped instead of clobbering newer state.
        var generation = Volatile.Read(ref _generation);

        // A failure (not connected, disconnect mid-fetch, engine error) surfaces as an empty catalog; the
        // session's disconnect handling owns connection state.
        var result = await _engine
            .InvokeAsync<AssetCatalogReply>("editor.listAssets", null, ct).ContinueOnAnyContext();
        var reply = result is { Success: true, Value: { } value } ? value : new AssetCatalogReply([], []);
        PublishIfCurrent(reply, generation);
    }

    private void OnSessionStateChanged(ConnectionState state)
    {
        // Every state change invalidates any in-flight refresh and is the only thing that moves the
        // generation, so the bump and the resulting publish both run on the UI thread for a consistent view.
        Dispatch.To(DispatchContext.UI, () =>
        {
            var generation = ++_generation;
            if (state == ConnectionState.Connected)
                RefreshAsync().FireAndForget();
            else
                Publish(new AssetCatalogReply([], []), generation);
        });
    }

    private void PublishIfCurrent(AssetCatalogReply reply, int generation) =>
        Dispatch.To(DispatchContext.UI, () => Publish(reply, generation));

    private void Publish(AssetCatalogReply reply, int generation)
    {
        // Drop a result whose connection generation has been superseded (a disconnect or newer refresh).
        if (generation != _generation)
            return;

        Assets = reply.Assets;
        Scripts = reply.Scripts;

        // The engine can legitimately report more than one asset with the same id (e.g. id 0 for entries
        // that have no stable id yet), so build the lookup defensively — a plain ToDictionary would throw
        // on the collision and take the whole catalog refresh down. First entry per id wins.
        var byId = new Dictionary<long, AssetInfo>(reply.Assets.Count);
        foreach (var asset in reply.Assets)
            byId.TryAdd(asset.Id, asset);
        _byId = byId;

        Changed?.Invoke();
    }
}
