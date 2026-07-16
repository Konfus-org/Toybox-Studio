using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Favorites;
using Icon = IconPacks.Avalonia.Lucide.PackIconLucideKind;
using KeyGesture = Avalonia.Input.KeyGesture;

namespace Toybox.Studio.MenuBar;

/// <summary>
/// One favoritable menu-bar action: its icon/title/shortcut and command, plus a star that pins it into the
/// bar's Favorites menu. Every bar item (the fixed File/Edit/Build/Debug actions, the data-driven Window
/// and Layout rows) is one of these, keyed by its <see cref="EditorAction"/> <see cref="Id"/> — the same id
/// the <c>FavoritesManager</c> stores under the <see cref="FavoritesManager.MenuBarHost"/> host, so a star
/// survives restarts and a favorite entry runs the exact same command. The command's own
/// <c>CanExecute</c> still gates the row, so a starred Save greys out with the real one. The shortcut hint
/// is mutable because it re-reads from the keymap when a binding changes.
/// </summary>
public sealed partial class MenuActionViewModel : ObservableObject
{
    private readonly FavoritesManager _favorites;

    public MenuActionViewModel(
        string id, string title, Icon icon, ICommand command, FavoritesManager favorites,
        KeyGesture? gesture = null)
    {
        Id = id;
        Title = title;
        Icon = icon;
        Command = command;
        _favorites = favorites;
        Gesture = gesture;
    }

    /// <summary>The action id — the favorite key and the row's identity.</summary>
    public string Id { get; }

    public string Title { get; }

    public Icon Icon { get; }

    /// <summary>The row's command; its <c>CanExecute</c> drives the item's enabled state.</summary>
    public ICommand Command { get; }

    /// <summary>The current keybinding, shown as the menu shortcut hint; refreshed on a rebind.</summary>
    [ObservableProperty]
    public partial KeyGesture? Gesture { get; set; }

    /// <summary>Whether this action is starred (drives the star's filled state and the Favorites menu).</summary>
    public bool IsFavorite => _favorites.IsFavorite(FavoritesManager.MenuBarHost, Id);

    [RelayCommand]
    private void ToggleFavorite() => _favorites.Toggle(FavoritesManager.MenuBarHost, Id);

    /// <summary>Re-reads the star state after the favorites store changes.</summary>
    public void RefreshFavorite() => OnPropertyChanged(nameof(IsFavorite));
}
