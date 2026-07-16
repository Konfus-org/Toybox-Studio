using Toybox.Studio.Toolbar;

namespace Toybox.Studio.Splitting;

/// <summary>
/// A leaf of a <see cref="SplitNode"/> tree: one hosted pane. It carries only persistable identity —
/// a stable <see cref="PaneId"/> and, for a pane that hosts overlay toolbars, that pane's own
/// <see cref="Toolbars"/> placements — so each pane in a split keeps its toolbar layout across a
/// restart. The live content (e.g. a viewport view-model) is minted by the container's pane source and
/// never serialized.
/// </summary>
public sealed class SplitLeaf : SplitNode
{
    /// <summary>A stable id for this pane, distinguishing its persisted per-pane state from its
    /// siblings'. Defaults to a fresh id; a restored leaf keeps the one it was saved with.</summary>
    public string PaneId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>This pane's overlay-toolbar placements, bound to the pane's content when it hosts an
    /// <see cref="IToolbarHost"/>. Empty (all defaults) for content without toolbars.</summary>
    public ToolbarDockStates Toolbars { get; set; } = new();
}
