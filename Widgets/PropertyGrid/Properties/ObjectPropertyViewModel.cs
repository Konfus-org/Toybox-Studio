using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json.Linq;

using Toybox.Studio.Services.EngineApi;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Nested struct/object property — a recursive sub-grid of child properties. Shows a state indicator like any
/// row: a struct reads as default exactly when all its children are (its <see cref="PropertyViewModel.IsModified"/>
/// is the reactive aggregate of its children).
/// </summary>
public sealed class ObjectPropertyViewModel : PropertyViewModel, IExpandable
{
    private readonly JToken? _object;

    // Nested items default collapsed (components and category groups default expanded); the user opens the
    // ones they care about. Keeps a deep component grid compact on selection.
    private bool _isExpanded;

    public ObjectPropertyViewModel(PropertyNode node, Action? commit, int depth = 0) : base(node)
    {
        _object = node.Value;

        // Leaf children float above nested struct/array sections (same rule as the grid's top level).
        // OrderChildren keeps a stable partition by node, so the VM order here and SyncCore's per-index zip
        // below both reorder the source nodes identically.
        Children = [];
        foreach (var child in OrderChildren(node.Children))
        {
            var childViewModel = PropertyViewModelFactory.Create(child, commit, depth + 1);
            childViewModel.PropertyChanged += OnChildChanged;
            Children.Add(childViewModel);
        }

        Disclosure = new DropdownPart(this);
        RecomputeModified();
    }

    public override bool IsComposite => true;

    /// <summary>The backing struct token, so the whole subtree can be compared/reset as a unit.</summary>
    public override JToken? CurrentValue => _object;

    public override bool HasChildren => true;

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

    // A struct is "set" exactly when one of its members is — recompute when any child's modified flag moves.
    private void OnChildChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(IsModified) or nameof(State))
            RecomputeModified();
    }

    private void RecomputeModified() => IsModified = Children.Any(child => child.IsModified);

    // Stable partition: leaf nodes first, then the nested struct/array nodes, declaration order kept within
    // each. Used by both the constructor and SyncCore so the VM order and the sync order always agree.
    private static IReadOnlyList<PropertyNode> OrderChildren(IEnumerable<PropertyNode> children) =>
        children.OrderBy(child => child.HasChildren ? 1 : 0).ToList();
}
