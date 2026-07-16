using Toybox.Studio.Events;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Favorites;

/// <summary>
/// The single owner of the user's starred items across every surface. Each "host" — the menu bar
/// (<see cref="MenuBarHost"/>) or a named context menu — is an independent bucket, loaded from its
/// <c>&lt;host&gt;.json</c> file the first time it is touched and mutated in memory thereafter. Starring an
/// item flips it for that host, persists just that host's file in the background, and dispatches a
/// <see cref="FavoritesChanged"/> carrying the host, so every open surface re-derives its stars without
/// polling. Hosts are independent, so the same id can be starred in one surface and not another (and a
/// plain label like "Copy" never collides across menus).
/// </summary>
public sealed class FavoritesManager
{
    /// <summary>The host key for the application menu bar's favorites (its own <c>MenuBar.json</c>).</summary>
    public const string MenuBarHost = "MenuBar";

    private readonly FavoritesStore _store;
    private readonly EventDispatcher _events;

    // Each host's starred ids, loaded lazily on first access (so a surface that is never opened never
    // reads its file). The list order is star order.
    private readonly Dictionary<string, List<string>> _hosts = new(StringComparer.Ordinal);

    public FavoritesManager(FavoritesStore store, EventDispatcher events)
    {
        _store = store;
        _events = events;
    }

    /// <summary>The starred item ids for a host, in the order they were starred (empty when none).</summary>
    public IReadOnlyList<string> Favorites(string host) => Ids(host);

    /// <summary>Whether <paramref name="id"/> is starred under <paramref name="host"/>.</summary>
    public bool IsFavorite(string host, string id) => Ids(host).Contains(id);

    /// <summary>Stars or un-stars an item under a host, then persists that host and notifies listeners.</summary>
    public void Toggle(string host, string id)
    {
        var ids = Ids(host);
        if (!ids.Remove(id))
            ids.Add(id);

        _store.SaveAsync(host, ids).FireAndForget();
        _events.Dispatch(new FavoritesChanged(host));
    }

    // The live list for a host, loaded from disk on first touch.
    private List<string> Ids(string host)
    {
        if (!_hosts.TryGetValue(host, out var ids))
            _hosts[host] = ids = _store.Load(host);
        return ids;
    }
}
