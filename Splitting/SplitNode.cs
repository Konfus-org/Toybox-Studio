namespace Toybox.Studio.Splitting;

/// <summary>
/// A node in a viewport-split tree: either a <see cref="SplitLeaf"/> (a single hosted pane) or a
/// <see cref="SplitBranch"/> (two children divided along an axis). The tree is a plain, serializable
/// data structure — the layout store round-trips it inside a panel's dock record, and the
/// <see cref="SplitLayout"/> helpers mutate it. It carries no UI and no live pane content (the
/// container maps each leaf to its content at runtime), so it stays free of any view dependency.
/// </summary>
public abstract class SplitNode;
