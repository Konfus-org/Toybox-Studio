namespace Toybox.Studio.Services;

/// <summary>
/// Keeps a UI-ready snapshot of the engine's active scene, refreshing automatically on connect.
/// All fetching and tree building happens off the UI thread.
/// </summary>
public sealed class WorldManager
{
    private readonly EngineSession _session;
    private readonly EngineJsonParser _parser;

    public WorldManager(EngineSession session, EngineJsonParser parser)
    {
        _session = session;
        _parser = parser;
        session.StateChanged += OnSessionStateChanged;
    }

    /// <summary>
    /// Raised with the new root nodes whenever the scene snapshot changes.
    /// </summary>
    public event Action<IReadOnlyList<WorldNode>>? SceneUpdated;

    public IReadOnlyList<WorldNode> RootNodes { get; private set; } = [];

    /// <summary>
    /// Finds a node by entity id in the current snapshot, or null if it is gone.
    /// </summary>
    public WorldNode? Find(ulong id) => Find(RootNodes, id);

    private static WorldNode? Find(IReadOnlyList<WorldNode> nodes, ulong id)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id)
                return node;

            if (Find(node.Children, id) is { } match)
                return match;
        }

        return null;
    }

    /// <summary>
    /// Re-fetches the scene from the engine. Failures surface as an empty scene.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var client = _session.Client;
        if (client is not { IsConnected: true })
        {
            Publish([]);
            return;
        }

        try
        {
            var description = await client.DescribeWorldAsync(ct).ContinueOnAnyContext();
            var roots = await Task.Run(() => _parser.ParseWorld(description), ct).ContinueOnAnyContext();
            Publish(roots);
        }
        catch (Exception)
        {
            // Connection dropped mid-fetch; the session's disconnect handling owns state.
            Publish([]);
        }
    }

    private void OnSessionStateChanged(EngineConnectionState state)
    {
        if (state == EngineConnectionState.Connected)
            RefreshAsync().FireAndForget();
        else
            Publish([]);
    }

    private void Publish(IReadOnlyList<WorldNode> roots)
    {
        RootNodes = roots;
        SceneUpdated?.Invoke(roots);
    }
}
