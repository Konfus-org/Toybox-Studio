using Avalonia.Controls;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.Theming;

namespace Toybox.Studio.Widgets.Theming;

/// <summary>
/// Modal dialog for authoring a new theme. Mirrors <see cref="MessageBoxWindow"/>'s ShowDialog pattern.
/// </summary>
public partial class ThemeCreatorWindow : Window
{
    public ThemeCreatorWindow()
    {
        InitializeComponent();
    }

    public static Task ShowAsync(Window owner, ThemeManager themes)
    {
        var viewModel = new ThemeCreatorViewModel(themes);
        var window = new ThemeCreatorWindow { DataContext = viewModel };
        // Own the switch prompt under this dialog so it nests correctly over the editor.
        viewModel.Confirm = (title, message) => Popups.ConfirmAsync(title, message, owner: window);
        viewModel.CloseRequested += window.Close;
        // Reverts the live preview if the user didn't switch — also covers closing via the title-bar button.
        window.Closed += (_, _) => viewModel.OnClosed();
        return window.ShowDialog(owner);
    }
}
