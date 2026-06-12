using Avalonia.Controls;

namespace Toybox.Studio.Shell;

/// <summary>
/// Settings UI, hosted as a floating dockable tool rather than a modal window. Owns launching the
/// modal Theme Creator on request, keeping the singleton view-model free of Window references.
/// </summary>
public partial class SettingsView : UserControl
{
    private SettingsViewModel? _subscribed;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_subscribed is not null)
            _subscribed.ThemeCreatorRequested -= OpenThemeCreator;

        _subscribed = DataContext as SettingsViewModel;
        if (_subscribed is not null)
            _subscribed.ThemeCreatorRequested += OpenThemeCreator;
    }

    private async void OpenThemeCreator()
    {
        if (_subscribed is null || TopLevel.GetTopLevel(this) is not Window owner)
            return;

        await ThemeCreatorWindow.ShowAsync(owner, _subscribed.Theme);
        // Pick up the newly created theme (and current selection) in the pickers.
        _subscribed.RefreshThemes();
    }
}
