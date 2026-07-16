namespace Toybox.Studio.Splitting;

/// <summary>
/// Supplies and disposes the live content behind each <see cref="SplitLeaf"/> in a split container. A
/// content-agnostic container calls this to mint a pane when a leaf first materializes (on a fresh
/// split or a layout restore) and to release one when a pane is joined away — keeping the container
/// itself free of any knowledge of what a pane <em>is</em> (a viewport, a preview, …). The minted
/// object is rendered through the host view's data templates.
/// </summary>
public interface IPaneSource
{
    /// <summary>Creates the content for <paramref name="leaf"/> (which carries the pane's persisted
    /// per-pane state, e.g. its toolbar placements, for the source to apply).</summary>
    object CreatePane(SplitLeaf leaf);

    /// <summary>Releases content returned by <see cref="CreatePane"/> whose pane has been removed, so
    /// its resources (e.g. an engine view stream) are torn down.</summary>
    void ReleasePane(object pane);
}
