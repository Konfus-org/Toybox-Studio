using Avalonia.Controls;
using Avalonia;
using Toybox.Studio.Splitting;

namespace Toybox.Studio.Splitting;

/// <summary>
/// A content-agnostic, Blender-style split container. It renders a <see cref="SplitLayout"/> tree into
/// nested grids — each <see cref="SplitLeaf"/> a pane hosting content minted by the bound
/// <see cref="PaneSource"/> (rendered through the host view's data templates), each
/// <see cref="SplitBranch"/> a live-draggable <see cref="SplitDivider"/> between its children — and owns
/// the structural edits the <see cref="SplitCornerBehavior"/> drives: splitting a pane (with a live
/// resize session that avoids rebuilding), and closing one back into its sibling. Attach the behavior to
/// panes and the container finds itself as their ancestor; nothing here knows what a pane <em>is</em>,
/// so the whole mechanism drops onto any view.
/// </summary>
public sealed class SplitContainer : Decorator
{
    /// <summary>The tree to render. Re-assigning (e.g. a restored layout) rebuilds the panes.</summary>
    public static readonly StyledProperty<SplitLayout?> LayoutProperty =
        AvaloniaProperty.Register<SplitContainer, SplitLayout?>(nameof(Layout));

    /// <summary>Mints and disposes each pane's content (see <see cref="IPaneSource"/>).</summary>
    public static readonly StyledProperty<IPaneSource?> PaneSourceProperty =
        AvaloniaProperty.Register<SplitContainer, IPaneSource?>(nameof(PaneSource));

    // The leaf a pane host stands for, stamped on every host so the behavior can resolve its leaf.
    private static readonly AttachedProperty<SplitLeaf?> LeafProperty =
        AvaloniaProperty.RegisterAttached<SplitContainer, Control, SplitLeaf?>("Leaf");

    private readonly Dictionary<SplitLeaf, object> _content = new();
    private readonly Dictionary<SplitLeaf, ContentControl> _hosts = new();
    private readonly Dictionary<SplitBranch, SplitDivider> _dividers = new();

    // The in-flight corner-split: the divider driving its live resize and the pane it created (so the
    // gesture can resize as the pointer moves, and undo cleanly if the drag is too small to keep).
    private SplitDivider? _liveDivider;
    private SplitLeaf? _liveCreated;

    public SplitLayout? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public IPaneSource? PaneSource
    {
        get => GetValue(PaneSourceProperty);
        set => SetValue(PaneSourceProperty, value);
    }

    internal static SplitLeaf? GetLeaf(Control host) => host.GetValue(LeafProperty);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LayoutProperty || change.Property == PaneSourceProperty)
            Rebuild();
    }

    /// <summary>Releases every pane's content (the host view-model calls this on dispose).</summary>
    public void ReleaseAll()
    {
        foreach (var content in _content.Values)
            PaneSource?.ReleasePane(content);
        _content.Clear();
        _hosts.Clear();
        _dividers.Clear();
        Child = null;
    }

    // --- Structural edits driven by the corner behavior ---

    /// <summary>
    /// Splits <paramref name="host"/>'s pane in two and starts a live resize session: the fresh pane
    /// takes the corner side (<paramref name="newFirst"/>) at <paramref name="ratio"/>, and subsequent
    /// <see cref="UpdateSplit"/> calls move the new divider under the pointer. Returns false if the host
    /// isn't a live pane.
    /// </summary>
    public bool BeginSplit(Control host, SplitOrientation orientation, double ratio, bool newFirst)
    {
        if (Layout is null || GetLeaf(host) is not { } target)
            return false;

        _liveCreated = Layout.Split(target, orientation, ratio, newFirst);
        Rebuild();

        var branch = Layout.ParentOf(_liveCreated);
        _liveDivider = branch is not null ? _dividers.GetValueOrDefault(branch) : null;
        return true;
    }

    /// <summary>Moves the in-flight split's divider to <paramref name="ratio"/> (live, no rebuild).</summary>
    public void UpdateSplit(double ratio) => _liveDivider?.Apply(
        Math.Clamp(ratio, SplitBranch.MinRatio, 1 - SplitBranch.MinRatio));

    /// <summary>Keeps the in-flight split and ends the session.</summary>
    public void CommitSplit()
    {
        _liveDivider = null;
        _liveCreated = null;
    }

    /// <summary>Undoes the in-flight split (a drag too small to keep), releasing the new pane.</summary>
    public void CancelSplit()
    {
        if (_liveCreated is not null)
            RemoveLeaf(_liveCreated);
        _liveDivider = null;
        _liveCreated = null;
    }

    /// <summary>Closes <paramref name="host"/>'s pane, growing its sibling to fill the freed region
    /// (the join). No-op on the last remaining pane.</summary>
    public void ClosePane(Control host)
    {
        if (GetLeaf(host) is { } leaf)
            RemoveLeaf(leaf);
    }

    /// <summary>Whether <paramref name="host"/>'s pane can be closed (it isn't the only one).</summary>
    public bool CanClose(Control host) =>
        Layout is not null && GetLeaf(host) is { } leaf && Layout.ParentOf(leaf) is not null;

    private void RemoveLeaf(SplitLeaf leaf)
    {
        if (Layout?.Remove(leaf) is null)
            return; // Root pane — nothing removed.

        if (_content.Remove(leaf, out var content))
            PaneSource?.ReleasePane(content);
        Rebuild();
    }

    // --- Rendering ---

    private void Rebuild()
    {
        _dividers.Clear();
        if (Layout is null)
        {
            Child = null;
            return;
        }

        // Reused hosts are still parented in the OLD tree (a prior grid, or this decorator when it was
        // the lone pane); orphan them first so re-adding them to the freshly built grids doesn't throw
        // "already has a visual parent".
        foreach (var host in _hosts.Values)
            Orphan(host);

        var live = new HashSet<SplitLeaf>();
        Child = BuildNode(Layout.Root, live);
        PruneContent(live);
    }

    private static void Orphan(Control control)
    {
        switch (control.Parent)
        {
            case Panel panel:
                panel.Children.Remove(control);
                break;
            case Decorator decorator:
                decorator.Child = null;
                break;
            case ContentControl content:
                content.Content = null;
                break;
        }
    }

    private Control BuildNode(SplitNode node, HashSet<SplitLeaf> live)
    {
        if (node is SplitLeaf leaf)
        {
            live.Add(leaf);
            return BuildLeaf(leaf);
        }

        var branch = (SplitBranch)node;
        var grid = new Grid();
        var horizontal = branch.Orientation == SplitOrientation.Horizontal;
        var first = new GridLength(branch.Ratio, GridUnitType.Star);
        var second = new GridLength(1 - branch.Ratio, GridUnitType.Star);
        var line = new GridLength(SplitDivider.Thickness, GridUnitType.Pixel);

        if (horizontal)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = first });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = line });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = second });
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = first });
            grid.RowDefinitions.Add(new RowDefinition { Height = line });
            grid.RowDefinitions.Add(new RowDefinition { Height = second });
        }

        var divider = new SplitDivider(branch, grid, branch.Orientation);
        _dividers[branch] = divider;

        var firstChild = BuildNode(branch.First, live);
        var secondChild = BuildNode(branch.Second, live);
        Place(firstChild, horizontal, 0);
        Place(divider, horizontal, 1);
        Place(secondChild, horizontal, 2);
        grid.Children.Add(firstChild);
        grid.Children.Add(divider);
        grid.Children.Add(secondChild);
        return grid;
    }

    private static void Place(Control control, bool horizontal, int index)
    {
        if (horizontal)
            Grid.SetColumn(control, index);
        else
            Grid.SetRow(control, index);
    }

    // Reuses a leaf's existing host across rebuilds so its content (and bound view-model) survives a
    // restructure; a new leaf gets a fresh host with its content minted from the pane source. Content is
    // (re)minted lazily so a host built before the pane source bound still fills once it's available.
    private Control BuildLeaf(SplitLeaf leaf)
    {
        if (!_content.TryGetValue(leaf, out var content) && PaneSource is { } source)
            _content[leaf] = content = source.CreatePane(leaf);

        if (_hosts.TryGetValue(leaf, out var existing))
        {
            existing.Content ??= content;
            return existing;
        }

        var host = new ContentControl { Content = content };
        host.SetValue(LeafProperty, leaf);
        SplitCornerBehavior.SetEnabled(host, true);
        _hosts[leaf] = host;
        return host;
    }

    private void PruneContent(HashSet<SplitLeaf> live)
    {
        foreach (var leaf in _hosts.Keys.Where(leaf => !live.Contains(leaf)).ToList())
            _hosts.Remove(leaf);

        foreach (var leaf in _content.Keys.Where(leaf => !live.Contains(leaf)).ToList())
        {
            if (_content.Remove(leaf, out var content))
                PaneSource?.ReleasePane(content);
        }
    }
}
