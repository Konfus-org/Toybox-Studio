using System.Linq;
using System.Reflection;
using Avalonia.Media;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Logging;
using Toybox.Studio.Project;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Ecs;

/// <summary>
/// One component type the engine can attach to an entity: its wire name (e.g. <c>transform</c>) and its header
/// badge. The display name is humanised in the UI; the icon is editor-defined — the <c>[Icon]</c> attribute on
/// the typed C# component class, resolved to a <see cref="Color"/> when the catalog is built. The engine's own
/// icon metadata is not read.
/// </summary>
public sealed record ComponentType(string Name, Icon Icon, Color? IconColor);

/// <summary>
/// The engine's reply to <c>reflect.catalog</c> — the component wire names. Icon metadata is editor-owned, so
/// the engine's is ignored on receipt.
/// </summary>
public sealed record ComponentCatalogReply(List<ComponentCatalogEntry> Components);

/// <summary>One entry in the <c>reflect.catalog</c> reply: a component's wire name.</summary>
public sealed record ComponentCatalogEntry(string Name);

/// <summary>
/// Keeps a UI-ready catalog of every component type the engine knows about (the engine sync catalog),
/// refreshed on connect, so the inspector's "Add Component" picker can list them. Mirrors the
/// <see cref="Toybox.Studio.Project.AssetCatalog"/>'s describe-on-connect pattern. On the first
/// populated refresh it also cross-checks the editor's typed <see cref="Component"/> classes against
/// engine truth (via <see cref="EngineSyncedComponentRegistry"/>) and warns on any drift.
/// </summary>
public sealed class ComponentCatalog
{
    private readonly Engine _engine;
    private readonly EngineSyncedComponentRegistry _registry;
    private readonly Logger _logger;

    // Bumped on every connection-state change so a slow reply can't publish over a newer (e.g. empty,
    // post-disconnect) state. All catalog state is published on the UI thread.
    private int _generation;

    // The typed-vs-engine drift check runs once (the type registry and engine registrations are both fixed for
    // a session), so a reconnect doesn't re-warn.
    private bool _validated;

    public ComponentCatalog(Session session, Engine engine, EngineSyncedComponentRegistry registry, Logger logger)
    {
        _engine = engine;
        _registry = registry;
        _logger = logger;
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
            .SendCommand<ComponentCatalogReply>(EngineMethods.SyncCatalog, null, ct)
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

        // The engine supplies the wire names; the icon is editor-defined — the [Icon] attribute on the typed C#
        // component the wire maps to (a wire with no typed class simply shows no icon).
        Components = reply.Components
            .Select(entry =>
            {
                var icon = _registry.TypeFor(entry.Name)?.GetCustomAttribute<IconAttribute>();
                return new ComponentType(entry.Name, icon?.Name ?? Icon.None, icon?.Color.ToColor());
            })
            .ToList();
        Changed?.Invoke();
        ValidateTypedComponents();
    }

    // Cross-checks the editor's typed reflected components against the engine catalog the first time it arrives
    // populated: a typed class whose derived wire name has no engine counterpart is a rename or typo (the typed
    // get/add path would silently never find it), so it's surfaced as a warning rather than failing quietly.
    private void ValidateTypedComponents()
    {
        if (_validated || Components.Count == 0)
            return;
        _validated = true;

        var known = new HashSet<string>(Components.Select(component => component.Name), StringComparer.Ordinal);
        var drift = _registry.Wires
            .Where(wire => !known.Contains(wire))
            .OrderBy(wire => wire, StringComparer.Ordinal)
            .ToList();
        if (drift.Count > 0)
            _logger.Warning(
                "Typed reflected components have no matching engine component type (a class-name vs engine-wire "
                + $"rename or typo): {string.Join(", ", drift)}.");
    }
}
