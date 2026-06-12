namespace Toybox.Studio.Services;

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
/// <see cref="WorldManager"/>'s describe-on-connect pattern.
/// </summary>
public sealed class AssetCatalog
{
    private readonly EngineSession _session;
    private Dictionary<long, AssetEntry> _byId = [];

    public AssetCatalog(EngineSession session)
    {
        _session = session;
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
        var client = _session.Client;
        if (client is not { IsConnected: true })
        {
            Publish(new AssetCatalogReply([], []));
            return;
        }

        try
        {
            var reply = await client.DescribeAssetsAsync(ct).ContinueOnAnyContext();
            Publish(reply);
        }
        catch (Exception)
        {
            // Connection dropped mid-fetch; the session's disconnect handling owns state.
            Publish(new AssetCatalogReply([], []));
        }
    }

    private void OnSessionStateChanged(EngineConnectionState state)
    {
        if (state == EngineConnectionState.Connected)
            RefreshAsync().FireAndForget();
        else
            Publish(new AssetCatalogReply([], []));
    }

    private void Publish(AssetCatalogReply reply)
    {
        Assets = reply.Assets;
        Scripts = reply.Scripts;
        _byId = reply.Assets.ToDictionary(asset => asset.Id);
        CatalogUpdated?.Invoke();
    }
}
