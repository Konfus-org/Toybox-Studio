namespace Toybox.Studio.NodeGraph;

/// <summary>
/// The persisted, editor-only state of one entity's node in the viewport overlay — the deviations from
/// the auto-placed, collapsed default, so a file only carries the nodes the user actually moved, expanded,
/// or hid. <see cref="OffsetX"/>/<see cref="OffsetY"/> are the user's drag in overlay pixels, added to the
/// node's projected screen anchor. Keyed by entity id, per world (see <see cref="NodeLayoutStore"/>).
/// </summary>
public sealed class NodeLayout
{
    /// <summary>The user's horizontal drag offset from the entity's projected anchor, in overlay pixels.</summary>
    public double OffsetX { get; set; }

    /// <summary>The user's vertical drag offset from the entity's projected anchor, in overlay pixels.</summary>
    public double OffsetY { get; set; }

    /// <summary>Whether the node is collapsed to its header (the default — keeps hundreds of nodes cheap by
    /// not building their inspectors until expanded).</summary>
    public bool Collapsed { get; set; } = true;

    /// <summary>Whether the user hid this node from the overlay.</summary>
    public bool Hidden { get; set; }

    /// <summary>Whether this layout still differs from the default (collapsed, no offset, visible). A node
    /// that matches the default needn't be written — the auto-node reproduces it.</summary>
    public bool IsDefault => !Hidden && Collapsed && OffsetX == 0 && OffsetY == 0;
}
