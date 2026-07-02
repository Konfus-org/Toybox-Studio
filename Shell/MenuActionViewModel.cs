using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Favorites;

namespace Toybox.Studio.Shell;

/// <summary>
/// One favoritable action on the application menu bar: an icon + label bound to a command, plus its star state.
/// The native menu items bind to these so each carries an icon and a star toggle, and a starred action is
/// surfaced in the generated "Favorites" menu — the same star feature the context menus have, applied to the
/// top toolbar. Favorites are scoped under the <see cref="Host"/> bucket in <see cref="FavoritesManager"/>.
/// </summary>
public sealed partial class MenuActionViewModel : ObservableObject
{
    /// <summary>The favorites bucket the menu-bar stars are stored under.</summary>
    public const string Host = "menubar";

    private readonly FavoritesManager _favorites;

    public MenuActionViewModel(
        string id,
        string label,
        Icon icon,
        Avalonia.Media.Color? iconColor,
        ICommand command,
        FavoritesManager favorites,
        object? parameter = null)
    {
        Id = id;
        Label = label;
        Icon = icon;
        IconColor = iconColor;
        Command = command;
        Parameter = parameter;
        _favorites = favorites;
    }

    public string Id { get; }

    public string Label { get; }

    public Icon Icon { get; }

    public Avalonia.Media.Color? IconColor { get; }

    public ICommand Command { get; }

    public object? Parameter { get; }

    public bool IsFavorite => _favorites.IsFavorite(Host, Id);

    /// <summary>The star glyph's colour: gold when starred, themed default otherwise.</summary>
    public Avalonia.Media.Color? StarColor => IsFavorite ? Toybox.Studio.Utils.Colors.Yellow : null;

    /// <summary>Re-reads the star state after the favorites store changes.</summary>
    public void RefreshFavorite()
    {
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(StarColor));
    }

    [RelayCommand]
    private void ToggleFavorite() => _favorites.Toggle(Host, Id);
}
