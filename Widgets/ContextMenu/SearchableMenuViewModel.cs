using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services.Favorites;
using Toybox.Studio.Utils.Extensions;

namespace Toybox.Studio.Widgets.ContextMenu;

/// <summary>
/// The view-model behind one open context menu: the searchable, favoritable list. Its rows are supplied by a
/// factory (so both the data-driven menus and the per-instance property menu build the same surface), a pinned
/// <see cref="Favorites"/> group mirrors the starred items for the host, and the search box narrows both.
/// Rebuilt fresh each time the menu opens; lives only while it is shown (disposing unsubscribes it from the
/// favorites store).
/// </summary>
public sealed partial class SearchableMenuViewModel : ObservableObject, IDisposable
{
    private readonly List<MenuEntryViewModel> _all;
    private readonly IDisposable _favoritesSubscription;

    /// <param name="buildEntries">
    /// Builds the menu's rows, given the close callback the rows invoke when chosen (passed in because each row
    /// needs it but it can only be bound to <c>this</c> after construction starts).
    /// </param>
    public SearchableMenuViewModel(
        Func<Action, IReadOnlyList<MenuEntryViewModel>> buildEntries, FavoritesManager favorites)
    {
        _all = buildEntries(Close).ToList();

        // Re-pin the Favorites group and refresh each star when the store changes (e.g. the user just starred
        // a row in this very menu). Listen fires immediately to do the first build.
        _favoritesSubscription = favorites.Listen(() =>
        {
            foreach (var entry in _all)
                entry.RefreshFavorite();
            Rebuild();
        });
    }

    /// <summary>Raised when the menu should close (an entry ran, or it asks to dismiss).</summary>
    public event Action? CloseRequested;

    /// <summary>The starred items for this host (pinned above the full list); empty when none match.</summary>
    public ObservableCollection<MenuEntryViewModel> Favorites { get; } = [];

    /// <summary>The full item list (in authored order), narrowed by the search box.</summary>
    public ObservableCollection<MenuEntryViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    [NotifyPropertyChangedFor(nameof(FavoritesShown))]
    public partial string Filter { get; set; } = "";

    /// <summary>Whether the "Favorites" submenu is expanded (collapsed by default; auto-opens while searching).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoritesShown))]
    public partial bool IsFavoritesExpanded { get; set; }

    public bool HasFavorites => Favorites.Count > 0;

    /// <summary>Whether the favorite rows are visible: when the submenu is expanded, or always while searching.</summary>
    public bool FavoritesShown => IsFavoritesExpanded || IsSearching;

    public bool IsSearching => Filter.Trim().Length > 0;

    /// <summary>Whether the menu has anything to show (an empty menu shouldn't be opened).</summary>
    public bool IsEmpty => _all.Count == 0;

    public void Close() => CloseRequested?.Invoke();

    public void Dispose() => _favoritesSubscription.Dispose();

    partial void OnFilterChanged(string value) => Rebuild();

    private void Rebuild()
    {
        var filter = Filter.Trim();
        var matching = _all.Where(entry => entry.IsSeparator || entry.Matches(filter)).ToList();

        // Favorites group: the starred, non-separator rows that match — never separators, never empty rows.
        Favorites.Clear();
        foreach (var entry in matching.Where(entry => entry.IsFavoritable && entry.IsFavorite))
            Favorites.Add(entry);

        // Full list: drop separators while searching (they'd float meaninglessly), and collapse leading,
        // trailing and doubled separators otherwise.
        Items.Clear();
        foreach (var entry in Tidy(matching, dropSeparators: filter.Length > 0))
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
