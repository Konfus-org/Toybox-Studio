using Toybox.Studio.Utils;
using Toybox.Studio.Services.EngineApi;

namespace Toybox.Studio.Services.World;

/// <summary>
/// One component type the engine can attach to an entity: its wire name (e.g. <c>transform</c>) and its
/// [[tbx::icon]] badge. The display name is humanised in the UI; the icon mirrors the component header's.
/// </summary>
public sealed record ComponentType(string Name, string? Icon = null, string? IconColor = null);

/// <summary>
/// The engine's reply to <c>editor.listComponentTypes</c>.
/// </summary>
public sealed record ComponentCatalogReply(List<ComponentType> Components);

/// <summary>
/// Keeps a UI-ready catalog of every component type the engine knows about, refreshed on connect, so the
/// inspector's "Add Component" picker can list them. Mirrors the
/// <see cref="Toybox.Studio.Services.Project.AssetCatalog"/>'s describe-on-connect pattern.
/// </summary>
public sealed class ComponentCatalog
{
    private readonly EngineRpc _engine;

    // Bumped on every connection-state change so a slow reply can't publish over a newer (e.g. empty,
    // post-disconnect) state. All catalog state is published on the UI thread.
    private int _generation;

    public ComponentCatalog(Session session, EngineRpc engine)
    {
        _engine = engine;
        session.StateChanged += OnSessionStateChanged;
    }

    /// <summary>Raised on the UI thread after the catalog is refreshed.</summary>
    public event Action? Changed;

    public IReadOnlyList<ComponentType> Components { get; private set; } = [];

    /// <summary>
    /// Re-fetches the catalog from the engine. Failures surface as an empty catalog.
    /// </summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var generation = Volatile.Read(ref _generation);
        var result = await _engine
            .InvokeAsync<ComponentCatalogReply>("editor.listComponentTypes", null, ct)
            .ContinueOnAnyContext();
        var reply = result is { Success: true, Value: { } value } ? value : new ComponentCatalogReply([]);
        Dispatch.To(DispatchContext.UI, () => Publish(reply, generation));
    }

    private void OnSessionStateChanged(ConnectionState state)
    {
        Dispatch.To(DispatchContext.UI, () =>
        {
            var generation = ++_generation;
            if (state == ConnectionState.Connected)
                RefreshAsync().FireAndForget();
            else
                Publish(new ComponentCatalogReply([]), generation);
        });
    }

    private void Publish(ComponentCatalogReply reply, int generation)
    {
        // Drop a result whose connection generation has been superseded (a disconnect or newer refresh).
        if (generation != _generation)
            return;

        Components = reply.Components;
        Changed?.Invoke();
    }
}
