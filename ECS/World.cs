using Toybox.Studio.EngineApi;

namespace Toybox.Studio.ECS;

/// <summary>
/// Keeps a UI-ready snapshot of the engine's active world, refreshing automatically on connect. The engine
/// owns the data: every snapshot is parsed fresh from a world.describe reply. All fetching and tree building
/// happens off the UI thread.
/// </summary>
public sealed class World
{
    private readonly EngineRpc _engine;
    private readonly JsonParser _parser;

    public World(Session session, EngineRpc engine, JsonParser parser)
    {
        _engine = engine;
        _parser = parser;
        session.StateChanged += OnSessionStateChanged;
    }

    /// <summary>
    /// Raised with the new root entities whenever the world snapshot changes.
    /// </summary>
    public event Action<IReadOnlyList<Entity>>? WorldUpdated;

    public IReadOnlyList<Entity> Roots { get; private set; } = [];

    /// <summary>
    /// Re-fetches the world from the engine. Failures surface as an empty world. The engine's disconnect
    /// handling owns connection state.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var result = await _engine.DescribeWorldAsync(ct).ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
        {
            Publish([]);
            return;
        }

        var roots = await Task.Run(() => _parser.ParseWorld(reply), ct).ContinueOnAnyContext();
        Publish(roots);
    }

    private void OnSessionStateChanged(ConnectionState state)
    {
        if (state == ConnectionState.Connected)
            RefreshAsync().FireAndForget();
        else
            Publish([]);
    }

    private void Publish(IReadOnlyList<Entity> roots)
    {
        Roots = roots;
        WorldUpdated?.Invoke(roots);
    }
}
