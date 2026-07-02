using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

using Toybox.Studio.EngineApi;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Nested struct/object property — a recursive sub-grid of child properties. Shows a state indicator like any
/// row: a struct reads as default exactly when all its children are (its <see cref="PropertyViewModel.IsModified"/>
/// is the reactive aggregate of its children). Children are built by pairing each child descriptor with the
/// member accessor the parent accessor hands out for it.
/// </summary>
public sealed class ObjectPropertyViewModel : PropertyViewModel, IExpandable
{
    // The child descriptors, ordered (leaves above nested structs) once, so the constructor and SyncCore both
    // walk them in the same order when pairing each with a fresh member accessor.
    private readonly IReadOnlyList<PropertyDescriptor> _childDescriptors;

    // Nested items default collapsed (components and category groups default expanded); the user opens the
    // ones they care about. Keeps a deep component grid compact on selection.
    private bool _isExpanded;

    public ObjectPropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor, int depth = 0)
        : base(descriptor, accessor)
    {
        // Leaf children float above nested struct/array sections (same rule as the grid's top level).
        // OrderChildren keeps a stable partition by descriptor, so the VM order here and SyncCore's per-index
        // zip below both reorder the source descriptors identically.
        _childDescriptors = OrderChildren(descriptor.Children);

        Children = [];
        foreach (var child in _childDescriptors)
        {
            var childViewModel = PropertyViewModelFactory.Create(child, accessor.Member(child), depth + 1);
            childViewModel.PropertyChanged += OnChildChanged;
            Children.Add(childViewModel);
        }

        Disclosure = new DropdownPart(this);
        RecomputeModified();
    }

    public override bool IsComposite => true;

    public override bool HasChildren => true;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public ObservableCollection<PropertyViewModel> Children { get; }

    protected override IEnumerable<PropertyViewModel> FilterChildren => Children;

    protected override bool SyncCore(IValueAccessor accessor)
    {
        // Zip each child row with a fresh member accessor drawn from the incoming accessor, matched by the same
        // descriptor order the constructor used.
        var synced = true;
        for (var index = 0; index < Children.Count; index++)
        {
            var child = _childDescriptors[index];
            synced &= Children[index].Sync(child, accessor.Member(child));
        }

        return synced;
    }

    // A struct is "set" exactly when one of its members is — recompute when any child's modified flag moves.
    private void OnChildChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(IsModified) or nameof(State))
            RecomputeModified();
    }

    private void RecomputeModified() => IsModified = Children.Any(child => child.IsModified);

    // Stable partition: leaf descriptors first, then the nested struct/array descriptors, declaration order kept
    // within each. Used by both the constructor and SyncCore so the VM order and the sync order always agree.
    private static IReadOnlyList<PropertyDescriptor> OrderChildren(IEnumerable<PropertyDescriptor> children) =>
        children.OrderBy(child => child.HasChildren ? 1 : 0).ToList();
}
