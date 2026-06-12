using System.Collections.ObjectModel;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// A run of properties under one [[tbx::category]] heading. The default (null) group renders header-less
/// at the top; named groups render under a collapsible header.
/// </summary>
public sealed class PropertyCategoryGroup
{
    public PropertyCategoryGroup(string? name, IEnumerable<PropertyViewModelBase> items)
    {
        Name = name;
        Items = new ObservableCollection<PropertyViewModelBase>(items);
    }

    public string? Name { get; }

    public bool HasHeader => !string.IsNullOrEmpty(Name);

    public ObservableCollection<PropertyViewModelBase> Items { get; }
}
