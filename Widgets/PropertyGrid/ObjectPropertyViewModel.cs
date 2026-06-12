using System.Collections.ObjectModel;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Nested struct/object property — a recursive sub-grid of child properties.
/// </summary>
public sealed class ObjectPropertyViewModel : PropertyViewModelBase
{
    public ObjectPropertyViewModel(PropertyNode node, Action? commit) : base(node)
    {
        Children = [];
        foreach (var child in node.Children)
            Children.Add(PropertyViewModelFactory.Create(child, commit));
    }

    public override bool IsComposite => true;

    public ObservableCollection<PropertyViewModelBase> Children { get; }
}
