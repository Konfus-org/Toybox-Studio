using Toybox.Studio.Splitting;

namespace Toybox.Studio.Docking;

/// <summary>
/// The dock record of a panel whose view-model hosts a split container (an <see cref="ISplitHost"/>,
/// e.g. the viewport grid): it rides the panel's <see cref="SplitLayout"/> inside the saved layout — the
/// layout store serializes the tree with type names, so the split (and each pane's own toolbar
/// placements) round-trips per panel instance. Mirrors <see cref="ToolbarPanelRecord"/>.
/// </summary>
public class SplitPanelRecord : DockPanelRecord
{
    /// <summary>The hosted split tree, handed to the view-model's <see cref="ISplitHost.BindSplitLayout"/>
    /// when the panel materializes.</summary>
    public SplitLayout Layout { get; set; } = new();
}
