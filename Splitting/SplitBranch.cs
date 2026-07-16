namespace Toybox.Studio.Splitting;

/// <summary>
/// An internal node dividing its region between two children along <see cref="Orientation"/>, with a
/// draggable divider between them. <see cref="Ratio"/> is <see cref="First"/>'s fraction of the shared
/// extent (0..1); <see cref="Second"/> takes the rest. Either child may itself be a branch, so the tree
/// nests arbitrarily.
/// </summary>
public sealed class SplitBranch : SplitNode
{
    /// <summary>The smallest fraction either child may be squeezed to when dragging the divider, so a
    /// pane can't be collapsed to nothing by a resize (a join is the way to remove one).</summary>
    public const double MinRatio = 0.05;

    public SplitOrientation Orientation { get; set; }

    /// <summary>The first child's fraction of the shared extent, clamped to
    /// [<see cref="MinRatio"/>, 1 − <see cref="MinRatio"/>].</summary>
    public double Ratio { get; set; } = 0.5;

    public SplitNode First { get; set; } = new SplitLeaf();

    public SplitNode Second { get; set; } = new SplitLeaf();
}
