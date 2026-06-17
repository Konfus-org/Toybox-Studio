using Toybox.Studio.Utils;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Models.Ecs;
namespace Toybox.Studio.Services.Project;

/// <summary>
/// One asset the engine knows about: a stable id, a display name, the asset's type (its file
/// extension, e.g. "mat"/"png"/"world"), and its project-relative path.
/// </summary>
public sealed record AssetEntry(long Id, string Name, string Type, string Path);

/// <summary>
/// One registered script type: its class name and serialization version.
/// </summary>
public sealed record ScriptEntry(string Name, int Version);

/// <summary>
/// The engine's reply to editor.listAssets.
/// </summary>
public sealed record AssetCatalogReply(List<AssetEntry> Assets, List<ScriptEntry> Scripts);

/// <summary>
/// Keeps a UI-ready catalog of the engine's assets and scripts, refreshed on connect, so the property
/// grid can resolve handle/script ids to names and populate asset pickers. Mirrors
/// <see cref="World"/>'s describe-on-connect pattern.
/// </summary>
public sealed class AssetCatalog
{
    private readonly EngineRpc _engine;
    private Dictionary<long, AssetEntry> _byId = [];

    public AssetCatalog(Session session, EngineRpc engine)
    {
        _engine = engine;
        session.StateChanged += OnSessionStateChanged;
    }

    /// <summary>
    /// Raised on the thread pool after the catalog is refreshed; widgets marshal to the UI thread.
    /// </summary>
    public event Action? CatalogUpdated;

    /// <summary>
    /// Raised when a script/asset link is activated (clicked). No-op until a host subscribes.
    /// </summary>
    public event Action<long>? AssetActivated;

    public IReadOnlyList<AssetEntry> Assets { get; private set; } = [];

    public IReadOnlyList<ScriptEntry> Scripts { get; private set; } = [];

    /// <summary>
    /// Assets matching any of the given type tokens (case-insensitive); all assets when none are given.
    /// </summary>
    public IReadOnlyList<AssetEntry> AssetsOfType(IReadOnlyList<string>? types)
    {
        if (types is not { Count: > 0 })
            return Assets;

        var wanted = new HashSet<string>(types, StringComparer.OrdinalIgnoreCase);
        return Assets.Where(asset => wanted.Contains(asset.Type)).ToList();
    }

    public AssetEntry? Resolve(long id) => _byId.GetValueOrDefault(id);

    public string? ResolveName(long id) => Resolve(id)?.Name;

    public void Activate(long id) => AssetActivated?.Invoke(id);

    /// <summary>
    /// Re-fetches the catalog from the engine. Failures surface as an empty catalog.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        // A failure (not connected, disconnect mid-fetch, engine error) surfaces as an empty catalog; the
        // session's disconnect handling owns connection state.
        var result = await _engine.ListAssetsAsync(ct).ContinueOnAnyContext();
        Publish(result is { Success: true, Value: { } reply } ? reply : new AssetCatalogReply([], []));
    }

    private void OnSessionStateChanged(ConnectionState state)
    {
        if (state == ConnectionState.Connected)
            RefreshAsync().FireAndForget();
        else
            Publish(new AssetCatalogReply([], []));
    }

    private void Publish(AssetCatalogReply reply)
    {
        Assets = reply.Assets;
        Scripts = reply.Scripts;

        // The engine can legitimately report more than one asset with the same id (e.g. id 0 for entries
        // that have no stable id yet), so build the lookup defensively — a plain ToDictionary would throw
        // on the collision and take the whole catalog refresh down. First entry per id wins.
        var byId = new Dictionary<long, AssetEntry>(reply.Assets.Count);
        foreach (var asset in reply.Assets)
            byId.TryAdd(asset.Id, asset);
        _byId = byId;

        CatalogUpdated?.Invoke();
    }
}
