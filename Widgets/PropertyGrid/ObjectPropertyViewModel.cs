using System.Collections.ObjectModel;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Nested struct/object property — a recursive sub-grid of child properties.
/// </summary>
public sealed class ObjectPropertyViewModel : PropertyViewModel
{
    public ObjectPropertyViewModel(PropertyNode node, Action? commit, int depth = 0) : base(node)
    {
        Children = [];
        foreach (var child in node.Children)
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
}
