using Newtonsoft.Json;

namespace Toybox.Studio.Services;

/// <summary>
/// Keeps a UI-ready snapshot of the engine's active scene, refreshing automatically on connect.
/// All fetching and tree building happens off the UI thread.
/// </summary>
public sealed class EngineSceneService
{
    private readonly EngineSessionService _session;

    public EngineSceneService(EngineSessionService session)
    {
        _session = session;
        session.StateChanged += OnSessionStateChanged;
    }

    /// <summary>Raised with the new root nodes whenever the scene snapshot changes.</summary>
    public event Action<IReadOnlyList<SceneNode>>? SceneUpdated;

    public IReadOnlyList<SceneNode> RootNodes { get; private set; } = [];

    /// <summary>Re-fetches the scene from the engine. Failures surface as an empty scene.</summary>
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
            var description = await client.DescribeWorldAsync(ct).ConfigureAwait(false);
            var roots = await Task.Run(() => BuildTree(description), ct).ConfigureAwait(false);
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
            _ = RefreshAsync();
        else
            Publish([]);
    }

    private void Publish(IReadOnlyList<SceneNode> roots)
    {
        RootNodes = roots;
        SceneUpdated?.Invoke(roots);
    }

    private static List<SceneNode> BuildTree(WorldDescription description)
    {
        var nodes = new Dictionary<ulong, SceneNode>();
        foreach (var entity in description.Entities)
        {
            nodes[entity.Id] = new SceneNode
            {
                Id = entity.Id,
                Name = string.IsNullOrEmpty(entity.Name) ? $"Entity {entity.Id}" : entity.Name,
                Tag = entity.Tag ?? "",
                Components = BuildComponents(entity),
            };
        }

        var roots = new List<SceneNode>();
        foreach (var entity in description.Entities)
        {
            var node = nodes[entity.Id];
            if (entity.Parent != 0 && nodes.TryGetValue(entity.Parent, out var parent))
                parent.Children.Add(node);
            else
                roots.Add(node);
        }

        SortRecursively(roots);
        return roots;
    }

    private static List<SceneComponent> BuildComponents(WorldEntity entity)
    {
        var components = new List<SceneComponent>();
        if (entity.Components is null)
            return components;

        foreach (var property in entity.Components.Properties())
            components.Add(new SceneComponent(property.Name, property.Value.ToString(Formatting.Indented)));

        components.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return components;
    }

    private static void SortRecursively(List<SceneNode> nodes)
    {
        nodes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        foreach (var node in nodes)
            SortRecursively(node.Children);
    }
}
