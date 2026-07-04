using Avalonia.Controls;

namespace Toybox.Studio;

/// <summary>
/// The VS-style start window: the recent projects to reopen beside get-started actions (open from
/// disk, continue without a project). The <see cref="ProjectPickerViewModel"/> is supplied via
/// <see cref="StyledElement.DataContext"/>; the launcher shows this window over the splash and closes
/// it once the view-model's choice completes.
/// </summary>
public partial class ProjectPickerWindow : Window
{
    public ProjectPickerWindow()
    {
        InitializeComponent();
    }
}
