using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System;
using Toybox.Studio.Events;
using Toybox.Studio.Favorites;
using Toybox.Studio.Utils.Search;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// The view-model behind one open context menu: the searchable, favoritable list. Its rows are supplied by a
/// factory (so every menu builds the same surface), a pinned <see cref="Favorites"/> group mirrors the starred
/// items for the host, and the search box narrows both. Rebuilt fresh each time the menu opens; lives only while
/// it is shown — it subscribes to <see cref="FavoritesChanged"/> for its own host (so starring a row live re-pins
/// the group) and disposing unsubscribes.
/// </summary>
public sealed partial class SearchableMenuViewModel : ObservableObject, IDisposable, IEventHandler<FavoritesChanged>
{
    private readonly List<MenuEntryViewModel> _all;
    private readonly string _host;
    private readonly EventDispatcher _events;

    /// <param name="host">The menu's favorites bucket — only <see cref="FavoritesChanged"/> for this host refreshes it.</param>
    /// <param name="buildEntries">
    /// Builds the menu's rows, given the close callback the rows invoke when chosen (passed in because each row
    /// needs it but it can only be bound to <c>this</c> after construction starts).
    /// </param>
    public SearchableMenuViewModel(
        string host, Func<Action, IReadOnlyList<MenuEntryViewModel>> buildEntries, EventDispatcher events)
    {
        _host = host;
        _events = events;
        _all = buildEntries(Close).ToList();

        // The filter matching runs through the shared search controller (separators always survive so the
        // list keeps its shape); building the Favorites group and tidying separators stays on the UI thread.
        Search = new SearchController<MenuEntryViewModel>(
            new PredicateSearchStrategy<MenuEntryViewModel>(
                (entry, query) => entry.IsSeparator || entry.Matches(query)),
            () => _all, ApplyResults);

        // React to the store changing (e.g. the user just starred a row in this very menu): refresh each star and
        // re-pin the Favorites group. Only this menu's host matters.
        events.Register<FavoritesChanged>(this);

        // Populate synchronously so the menu has content the instant it opens (no debounce on first show).
        ApplyResults(_all);
    }

    /// <summary>Drives the search box: its <c>Query</c> is the filter, <c>IsBusy</c> the spinner.</summary>
    public SearchController<MenuEntryViewModel> Search { get; }

    /// <summary>Raised when the menu should close (an entry ran, or it asks to dismiss).</summary>
    public event Action? CloseRequested;

    /// <summary>The starred items for this host (pinned above the full list); empty when none match.</summary>
    public ObservableCollection<MenuEntryViewModel> Favorites { get; } = [];

    /// <summary>The full item list (in authored order), narrowed by the search box.</summary>
    public ObservableCollection<MenuEntryViewModel> Items { get; } = [];

    /// <summary>The search text (proxied to the controller), narrowing both lists.</summary>
    public string Filter
    {
        get => Search.Query;
        set
        {
            if (Search.Query == value)
                return;

            Search.Query = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSearching));
            OnPropertyChanged(nameof(FavoritesShown));
        }
    }

    /// <summary>Whether the "Favorites" submenu is expanded (collapsed by default; auto-opens while searching).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoritesShown))]
    public partial bool IsFavoritesExpanded { get; set; }

    public bool HasFavorites => Favorites.Count > 0;

    /// <summary>Whether the favorite rows are visible: when the submenu is expanded, or always while searching.</summary>
    public bool FavoritesShown => IsFavoritesExpanded || IsSearching;

    public bool IsSearching => Search.Query.Trim().Length > 0;

    /// <summary>Whether the menu has anything to show (an empty menu shouldn't be opened).</summary>
    public bool IsEmpty => _all.Count == 0;

    public void Close() => CloseRequested?.Invoke();

    public void Dispose()
    {
        _events.Unregister<FavoritesChanged>(this);
        Search.Dispose();
    }

    public void Handle(in FavoritesChanged evt)
    {
        if (evt.Host != _host)
            return;

        foreach (var entry in _all)
            entry.RefreshFavorite();
        Search.Refresh();
    }

    // (UI thread) Splits the matched rows into the pinned Favorites group and the tidied full list.
    private void ApplyResults(IReadOnlyList<MenuEntryViewModel> matching)
    {
        var searching = IsSearching;

        // Favorites group: the starred, non-separator rows that match — never separators, never empty rows.
        Favorites.Clear();
        foreach (var entry in matching.Where(entry => entry.IsFavoritable && entry.IsFavorite))
            Favorites.Add(entry);

        // Full list: drop separators while searching (they'd float meaninglessly), and collapse leading,
        // trailing and doubled separators otherwise.
        Items.Clear();
        foreach (var entry in Tidy(matching, dropSeparators: searching))
            Items.Add(entry);

        OnPropertyChanged(nameof(HasFavorites));
    }

    // Removes redundant separators so the menu never shows a leading/trailing divider or two in a row. Each
    // pending separator is the specific (distinct) instance from the definition, so none is emitted twice.
    private static IEnumerable<MenuEntryViewModel> Tidy(
        IReadOnlyList<MenuEntryViewModel> entries, bool dropSeparators)
    {
        MenuEntryViewModel? pendingSeparator = null;
        var emittedAny = false;
        foreach (var entry in entries)
        {
            if (entry.IsSeparator)
            {
                if (!dropSeparators && emittedAny)
                    pendingSeparator = entry;
                continue;
            }

            if (pendingSeparator is not null)
            {
                yield return pendingSeparator;
                pendingSeparator = null;
            }

            emittedAny = true;
            yield return entry;
        }
    }
}
