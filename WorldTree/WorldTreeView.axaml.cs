using Avalonia.Controls;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.WorldTree;

/// <summary>
/// The World Tree (Hierarchy) dockable — the active world's entity tree over its inspector. A singleton
/// panel seeded on the window's left column (<see cref="DockSlot.Left"/>), beside the World Viewer. Its
/// view-model resolves by the <c>XxxView → XxxViewModel</c> convention.
/// </summary>
[Dockable(Title = "Hierarchy", Icon = "ListTree", Slot = DockSlot.Left, Proportion = 0.22)]
public partial class WorldTreeView : UserControl
{
    public WorldTreeView()
    {
        InitializeComponent();
    }
}
