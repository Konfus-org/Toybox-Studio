using Toybox.Studio.Toolbar;

namespace Toybox.Studio.Docking;

/// <summary>
/// The dock record of a panel whose view-model hosts movable overlay toolbars (an
/// <see cref="IToolbarHost"/>, e.g. a viewport): it rides the toolbars' placements inside the saved
/// layout — the layout store serializes the tree with type names, so this subclass round-trips per
/// panel instance and each viewport keeps its own toolbar placements.
/// </summary>
public class ToolbarPanelRecord : DockPanelRecord
{
    /// <summary>The hosted toolbars' persisted placements (keyed per toolbar), handed to the
    /// view-model's <see cref="IToolbarHost.BindToolbars"/> when the panel materializes.</summary>
    public ToolbarDockStates Toolbars { get; set; } = new();
}
