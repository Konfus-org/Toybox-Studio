using Avalonia.Controls;

namespace Toybox.Studio.Widgets.Theming;

/// <summary>
/// Renders the Themes list property inside the editor-settings grid: a banded parent header with an add
/// affordance, then a depth-1 row per theme. Drawn with the grid's own PropertyRow chrome so it matches the
/// Engine / Build sections exactly. See <see cref="ThemePropertyViewModel"/>.
/// </summary>
public partial class ThemePropertyView : UserControl
{
    public ThemePropertyView()
    {
        InitializeComponent();
    }
}
