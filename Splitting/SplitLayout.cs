namespace Toybox.Studio.Splitting;

/// <summary>
/// A viewport-split layout: the split tree plus the pure structural edits the container drives —
/// splitting a pane in two, joining two panes back into one, and locating a node's parent. It holds no
/// UI state; the container maps each <see cref="SplitLeaf"/> to its live content and re-reads the tree
/// after each edit. A fresh layout is a single pane. The layout store serializes this whole object
/// inside a panel's dock record, so a saved split (and each pane's toolbar placements) round-trips.
/// </summary>
public sealed class SplitLayout
{
    /// <summary>The tree root — a lone <see cref="SplitLeaf"/> until the first split.</summary>
    public SplitNode Root { get; set; } = new SplitLeaf();

    /// <summary>Every pane in the layout, left/top to right/bottom.</summary>
    public IEnumerable<SplitLeaf> Leaves() => LeavesOf(Root);

    private static IEnumerable<SplitLeaf> LeavesOf(SplitNode node)
    {
        if (node is SplitLeaf leaf)
        {
            yield return leaf;
            yield break;
        }

        var branch = (SplitBranch)node;
        foreach (var child in LeavesOf(branch.First))
            yield return child;
        foreach (var child in LeavesOf(branch.Second))
            yield return child;
    }

    /// <summary>The branch that has <paramref name="node"/> as a direct child, or null when
    /// <paramref name="node"/> is the root.</summary>
    public SplitBranch? ParentOf(SplitNode node) => ParentIn(Root, node);

    private static SplitBranch? ParentIn(SplitNode current, SplitNode target)
    {
        if (current is not SplitBranch branch)
            return null;
        if (ReferenceEquals(branch.First, target) || ReferenceEquals(branch.Second, target))
            return branch;
        return ParentIn(branch.First, target) ?? ParentIn(branch.Second, target);
    }

    /// <summary>
    /// Splits <paramref name="target"/> into two, inserting a branch in its place: the existing pane
    /// keeps its content, a fresh <see cref="SplitLeaf"/> takes the other side. <paramref name="newFirst"/>
    /// puts the new pane before (left/top of) the existing one; <paramref name="ratio"/> is the first
    /// child's fraction. Returns the new pane so the caller can populate its content.
    /// </summary>
    public SplitLeaf Split(SplitLeaf target, SplitOrientation orientation, double ratio, bool newFirst)
    {
        var created = new SplitLeaf();
        var branch = new SplitBranch
        {
            Orientation = orientation,
            Ratio = Math.Clamp(ratio, SplitBranch.MinRatio, 1 - SplitBranch.MinRatio),
            First = newFirst ? created : target,
            Second = newFirst ? target : created,
        };

        Replace(target, branch);
        return created;
    }

    /// <summary>
    /// Removes <paramref name="pane"/> and collapses its parent branch to the sibling, so the sibling
    /// grows to fill the freed region (the join / close operation). No-op on the root (the last pane
    /// can't be removed). Returns the sibling subtree that took over, or null when nothing was removed.
    /// </summary>
    public SplitNode? Remove(SplitLeaf pane)
    {
        if (ParentOf(pane) is not { } parent)
            return null;

        var sibling = ReferenceEquals(parent.First, pane) ? parent.Second : parent.First;
        Replace(parent, sibling);
        return sibling;
    }

    // Swaps oldNode for newNode wherever it sits in the tree (root or one child slot of some branch).
    private void Replace(SplitNode oldNode, SplitNode newNode)
    {
        if (ReferenceEquals(Root, oldNode))
        {
            Root = newNode;
            return;
        }

        if (ParentOf(oldNode) is not { } parent)
            return;

        if (ReferenceEquals(parent.First, oldNode))
            parent.First = newNode;
        else
            parent.Second = newNode;
    }
}
