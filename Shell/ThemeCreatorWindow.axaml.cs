using Avalonia.Controls;
using Toybox.Studio.Services;

namespace Toybox.Studio.Shell;

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
        viewModel.CloseRequested += window.Close;
        return window.ShowDialog(owner);
    }
}
