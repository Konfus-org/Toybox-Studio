using Avalonia.Controls;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.Coding;

/// <summary>
/// The Coder dockable: a thin host that templates a <see cref="CoderView"/> over the panel view-model's
/// editor. A singleton panel — opening a script focuses the one editor and adds tabs rather than spawning
/// duplicate windows. Opens docked in the window's center area (<see cref="DockSlot.Center"/>) — a script
/// opens as a tab beside the World Viewer, not a floating window — while staying out of the default layout
/// until a file is opened.
/// </summary>
[Dockable(Title = "Coder", Icon = "Code", Slot = DockSlot.Center)]
public partial class CoderPanelView : UserControl
{
    public CoderPanelView()
    {
        InitializeComponent();
    }
}
