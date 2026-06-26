using System;
using System.Collections.Generic;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.Favorites;

/// <summary>
/// The single owner of the user's starred context-menu and toolbar items. Loads <see cref="FavoritesFile"/>
/// once at startup and exposes/mutates it; starring an item flips it for that host and persists in the
/// background. Raises <see cref="Changed"/> (on the UI thread) so every open menu and the toolbar's Favorites
/// section re-derive their starred list without polling — see <see cref="IListenable"/> and its
/// <c>Listen</c> extension. Favorites are scoped per host (a menu id, or <c>"toolbar"</c>)
/// so the same item id can be starred independently in different menus.
/// </summary>
public sealed class FavoritesManager : IListenable
{
    /// <summary>The host key for the application toolbar's favorites (distinct from any context menu).</summary>
    public const string ToolbarHost = "toolbar";

    private readonly FavoritesFile _file = FavoritesFile.Load();

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>The starred item ids for a host, in the order they were starred (empty when none).</summary>
    public IReadOnlyList<string> Favorites(string host) =>
        _file.Hosts.TryGetValue(host, out var ids) ? ids : [];

    /// <summary>Whether <paramref name="id"/> is starred under <paramref name="host"/>.</summary>
    public bool IsFavorite(string host, string id) =>
        _file.Hosts.TryGetValue(host, out var ids) && ids.Contains(id);

    /// <summary>Stars or un-stars an item under a host, then persists and notifies.</summary>
    public void Toggle(string host, string id)
    {
        if (!_file.Hosts.TryGetValue(host, out var ids))
            _file.Hosts[host] = ids = [];

        if (!ids.Remove(id))
            ids.Add(id);
        if (ids.Count == 0)
            _file.Hosts.Remove(host);

        _file.SaveAsync().FireAndForget();
        Dispatch.To(DispatchContext.UI, () => Changed?.Invoke());
    }
}
