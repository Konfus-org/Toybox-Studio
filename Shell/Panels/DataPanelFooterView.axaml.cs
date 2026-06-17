using Avalonia.Controls;

namespace Toybox.Studio.Shell.Panels;

/// <summary>
/// The shared Save/Cancel footer for a buffered <see cref="DataPanel"/>. Dropped into a panel view's outer
/// layout as <c>DockPanel.Dock="Bottom"</c>; it inherits the panel's view-model as its DataContext.
/// </summary>
public partial class DataPanelFooterView : UserControl
{
    public DataPanelFooterView()
    {
        InitializeComponent();
    }
}
