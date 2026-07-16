using CommunityToolkit.Mvvm.ComponentModel;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// One card of the property grid: the run of top-level nodes that share a <see cref="PropertyNode.Category"/>,
/// under a collapsible accent header. The uncategorized nodes form the one header-less group that floats above
/// the named cards (<see cref="HasHeader"/> false, <see cref="Name"/> null), so a target with no
/// <c>[Category]</c> attributes renders as a single flat card exactly as before. The grid view-model rebuilds
/// these from its nodes; the view renders one <c>toyCard</c> per group.
/// </summary>
public sealed partial class PropertyCategoryGroup : ObservableObject
{
    public PropertyCategoryGroup(string? name, IReadOnlyList<PropertyNode> items)
    {
        Name = name;
        Items = items;
    }

    /// <summary>The category heading, or null for the header-less uncategorized group.</summary>
    public string? Name { get; }

    public bool HasHeader => Name is not null;

    public IReadOnlyList<PropertyNode> Items { get; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;
}
