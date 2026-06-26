using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Toybox.Studio.Widgets.ContextMenu;

/// <summary>
/// The searchable, favoritable context-menu surface shown inside a flyout (see <see cref="MenuOpenBehavior"/>):
/// a search box over a pinned Favorites group and the full item list. Pure view — its rows command into the
/// <see cref="SearchableMenuViewModel"/>.
/// </summary>
public partial class SearchableMenuView : UserControl
{
    public SearchableMenuView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
