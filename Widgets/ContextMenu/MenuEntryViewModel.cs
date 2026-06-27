using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services.Favorites;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Widgets.ContextMenu;

/// <summary>
/// One row in a searchable context menu: wraps a code-defined <see cref="MenuItem"/> and exposes its
/// icon/label/shortcut, whether it is a separator, and its star (favorite) state for the owning host. Choosing
/// the row runs its action and closes the menu. The item's action is a plain delegate (the editor menus and the
/// property-row menu both supply one), so this row type is agnostic to where the action comes from.
/// </summary>
public sealed partial class MenuEntryViewModel : ObservableObject
{
    private readonly MenuItem _item;
    private readonly string _host;
    private readonly FavoritesManager _favorites;
    private readonly Action _close;

    public MenuEntryViewModel(MenuItem item, string host, FavoritesManager favorites, Action close)
    {
        _item = item;
        _host = host;
        _favorites = favorites;
        _close = close;
    }

    /// <summary>The underlying item (its id is the reconcile / favorite key).</summary>
    public MenuItem Model => _item;

    public string Id => _item.Id;

    public string Label => _item.Label;

    public string IconName => _item.Icon;

    public string? IconColor => _item.IconColor;

    public string? Gesture => _item.Gesture;

    public bool HasGesture => !string.IsNullOrEmpty(_item.Gesture);

    public bool IsSeparator => _item.IsSeparator;

    /// <summary>Whether this row can be starred — separators can't.</summary>
    public bool IsFavoritable => !_item.IsSeparator && _item.Id.Length > 0;

    public bool IsFavorite => IsFavoritable && _favorites.IsFavorite(_host, _item.Id);

    /// <summary>The star glyph's colour token: gold when starred, themed default otherwise.</summary>
    public string? StarColor => IsFavorite ? "YELLOW" : null;

    /// <summary>True when the menu's search box matches this row (always true for an empty filter).</summary>
    public bool Matches(string filter) =>
        filter.Length == 0
        || _item.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (_item.Keywords?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>Re-reads the star state after the favorites store changes.</summary>
    public void RefreshFavorite()
    {
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(StarColor));
    }

    [RelayCommand]
    private void Run()
    {
        if (_item.IsSeparator)
            return;

        _close();
        _item.Run?.Invoke().FireAndForget();
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (IsFavoritable)
            _favorites.Toggle(_host, _item.Id);
    }
}
