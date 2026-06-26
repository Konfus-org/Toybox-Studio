using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services.Commands;
using Toybox.Studio.Services.Favorites;
using Toybox.Studio.Utils;
using Toybox.Studio.Widgets.Toolbar;

namespace Toybox.Studio.Widgets.ContextMenu;

/// <summary>
/// One row in a searchable context menu: wraps a <see cref="MenuEntry"/> and exposes its icon/label/shortcut,
/// whether it is a separator, and its star (favorite) state for the owning host. Choosing the row runs its
/// action and closes the menu (<see cref="Closed"/>). The action is either a data-driven
/// <see cref="ToolCommand"/> (run through the shared <see cref="ToolCommandRunner"/> with the menu's
/// <see cref="MenuContext"/>) or a local delegate — the latter lets per-instance menus (e.g. a property row's
/// copy/paste/reset, which act on a specific view-model rather than the global selection) reuse this same
/// searchable, favoritable surface.
/// </summary>
public sealed partial class MenuEntryViewModel : ObservableObject
{
    private readonly MenuEntry _entry;
    private readonly string _host;
    private readonly FavoritesManager _favorites;
    private readonly Func<Task> _run;
    private readonly Action _close;

    /// <summary>A data-driven entry: runs its <see cref="MenuEntry.Command"/> through the shared runner.</summary>
    public MenuEntryViewModel(
        MenuEntry entry,
        string host,
        ToolCommandRunner runner,
        FavoritesManager favorites,
        MenuContext context,
        Action close)
        : this(entry, host, favorites, () => runner.RunAsync(entry.Command, CancellationToken.None, context), close)
    {
    }

    /// <summary>A local entry: runs <paramref name="run"/> directly (a per-instance view-model action).</summary>
    public MenuEntryViewModel(
        MenuEntry entry, string host, FavoritesManager favorites, Func<Task> run, Action close)
    {
        _entry = entry;
        _host = host;
        _favorites = favorites;
        _run = run;
        _close = close;
    }

    /// <summary>The underlying entry (its id is the reconcile / favorite key).</summary>
    public MenuEntry Model => _entry;

    public string Id => _entry.Id;

    public string Label => _entry.Label;

    public string IconName => _entry.Icon;

    public string? IconColor => _entry.IconColor;

    public string? Gesture => _entry.InputGesture;

    public bool HasGesture => !string.IsNullOrEmpty(_entry.InputGesture);

    public bool IsSeparator => _entry.IsSeparator;

    /// <summary>Whether this entry can be starred — separators can't.</summary>
    public bool IsFavoritable => !_entry.IsSeparator && _entry.Id.Length > 0;

    public bool IsFavorite => IsFavoritable && _favorites.IsFavorite(_host, _entry.Id);

    /// <summary>The star glyph's colour token: gold when starred, themed default otherwise.</summary>
    public string? StarColor => IsFavorite ? "YELLOW" : null;

    /// <summary>True when the menu's search box matches this row (always true for an empty filter).</summary>
    public bool Matches(string filter) =>
        filter.Length == 0
        || _entry.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || (_entry.Keywords?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>Re-reads the star state after the favorites store changes.</summary>
    public void RefreshFavorite()
    {
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(StarColor));
    }

    [RelayCommand]
    private void Run()
    {
        if (_entry.IsSeparator)
            return;

        _close();
        _run().FireAndForget();
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (IsFavoritable)
            _favorites.Toggle(_host, _entry.Id);
    }
}
