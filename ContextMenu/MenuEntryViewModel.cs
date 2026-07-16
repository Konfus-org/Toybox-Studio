using Avalonia.Media.Immutable;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using Toybox.Studio.Favorites;
using Toybox.Studio.Utils;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// One row in a searchable context menu: wraps a code-defined <see cref="MenuItem"/> and exposes its
/// icon/label/shortcut, whether it is a separator, and its star (favorite) state for the owning host. Choosing
/// the row runs its action and closes the menu. The item's action is a plain delegate, so this row type is
/// agnostic to where the action comes from.
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

    /// <summary>The underlying item (its label is the favorite key).</summary>
    public MenuItem Model => _item;

    public string Label => _item.Label;

    public Icon IconName => _item.Icon;

    /// <summary>The icon's brush when the row carries a semantic colour (a red delete, a green add); null lets the
    /// row template fall back to the themed text colour.</summary>
    public IBrush? IconBrush =>
        _item.IconColor is { } color ? new ImmutableSolidColorBrush(color) : null;

    public string? Gesture => _item.Gesture;

    public bool HasGesture => !string.IsNullOrEmpty(_item.Gesture);

    /// <summary>Whether the row is clickable; a disabled row renders greyed out.</summary>
    public bool IsEnabled => _item.IsEnabled;

    /// <summary>Tooltip shown on a greyed-out row explaining why it's unavailable; null when enabled.</summary>
    public string? DisabledReason => _item.DisabledReason;

    public bool IsSeparator => _item.IsSeparator;

    /// <summary>Whether this row can be starred — separators (and any label-less row) can't.</summary>
    public bool IsFavoritable => !_item.IsSeparator && _item.Label.Length > 0;

    public bool IsFavorite => IsFavoritable && _favorites.IsFavorite(_host, _item.Label);

    /// <summary>True when the menu's search box matches this row (always true for an empty filter).</summary>
    public bool Matches(string filter) =>
        filter.Length == 0
        || _item.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (_item.Keywords?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>Re-reads the star state after the favorites store changes.</summary>
    public void RefreshFavorite() => OnPropertyChanged(nameof(IsFavorite));

    [RelayCommand]
    private void Run()
    {
        if (_item.IsSeparator || !_item.IsEnabled)
            return;

        _close();
        _item.Run?.Invoke().FireAndForget();
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (IsFavoritable)
            _favorites.Toggle(_host, _item.Label);
    }
}
