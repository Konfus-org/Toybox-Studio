using System.Collections.ObjectModel;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Nested struct/object property — a recursive sub-grid of child properties.
/// </summary>
public sealed class ObjectPropertyViewModel : PropertyViewModelBase
{
    public ObjectPropertyViewModel(PropertyNode node, Action? commit, int depth = 0) : base(node)
    {
        Children = [];
        foreach (var child in node.Children)
            Children.Add(PropertyViewModelFactory.Create(child, commit, depth + 1));
    }

    public override bool IsComposite => true;

    public override bool HasChildren => true;

    private bool _isExpanded = true;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public ObservableCollection<PropertyViewModelBase> Children { get; }
}
