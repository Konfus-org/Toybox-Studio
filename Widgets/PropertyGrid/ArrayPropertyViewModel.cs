using System.Collections.ObjectModel;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Array property — an expandable list of element sub-widgets.
/// </summary>
public sealed class ArrayPropertyViewModel : PropertyViewModelBase
{
    public ArrayPropertyViewModel(PropertyNode node, Action? commit) : base(node)
    {
        Items = [];
        foreach (var child in node.Children)
            Items.Add(PropertyViewModelFactory.Create(child, commit));

        Summary = $"{Items.Count} item{(Items.Count == 1 ? "" : "s")}";
    }

    public override bool IsComposite => true;

    public ObservableCollection<PropertyViewModelBase> Items { get; }

    public string Summary { get; }
}
