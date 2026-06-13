using System.Collections.ObjectModel;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Array property — an expandable list of element sub-widgets.
/// </summary>
public sealed class ArrayPropertyViewModel : PropertyViewModelBase
{
    public ArrayPropertyViewModel(PropertyNode node, Action? commit, int depth = 0) : base(node)
    {
        Items = [];
        foreach (var child in node.Children)
            Items.Add(PropertyViewModelFactory.Create(child, commit, depth + 1));

        Summary = $"{Items.Count} item{(Items.Count == 1 ? "" : "s")}";
    }

    public override bool IsComposite => true;

    public override bool HasChildren => true;

    private bool _isExpanded = true;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public ObservableCollection<PropertyViewModelBase> Items { get; }

    public string Summary { get; }
}
