using System.Collections.ObjectModel;
using System.Linq;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Nested struct/object property — a recursive sub-grid of child properties.
/// </summary>
public sealed class ObjectPropertyViewModel : PropertyViewModel
{
    public ObjectPropertyViewModel(PropertyNode node, Action? commit, int depth = 0) : base(node)
    {
        // Leaf children float above nested struct/array sections (same rule as the grid's top level).
        // OrderChildren keeps a stable partition by node, so the VM order here and SyncCore's per-index zip
        // below both reorder the source nodes identically.
        Children = [];
        foreach (var child in OrderChildren(node.Children))
            Children.Add(PropertyViewModelFactory.Create(child, commit, depth + 1));
    }

    public override bool IsComposite => true;

    public override bool HasChildren => true;

    // Nested items default collapsed (components and category groups default expanded); the user opens the
    // ones they care about. Keeps a deep component grid compact on selection.
    private bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public ObservableCollection<PropertyViewModel> Children { get; }

    protected override IEnumerable<PropertyViewModel> FilterChildren => Children;

    protected override bool SyncCore(PropertyNode node)
    {
        // A changed child count means the struct's shape moved (not just its values); rebuild rather than
        // risk zipping mismatched rows.
        if (node.Children.Count != Children.Count)
            return false;

        // Reorder the incoming nodes the same way the constructor ordered the VMs, so the per-index zip pairs
        // each child with its own node even though leaves were floated above the nested sections.
        var ordered = OrderChildren(node.Children);
        var synced = true;
        for (var index = 0; index < Children.Count; index++)
            synced &= Children[index].Sync(ordered[index]);
        return synced;
    }

    // Stable partition: leaf nodes first, then the nested struct/array nodes, declaration order kept within
    // each. Used by both the constructor and SyncCore so the VM order and the sync order always agree.
    private static IReadOnlyList<PropertyNode> OrderChildren(IEnumerable<PropertyNode> children) =>
        children.OrderBy(child => child.HasChildren ? 1 : 0).ToList();
}
