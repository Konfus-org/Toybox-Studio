using System.Collections.ObjectModel;
using Toybox.Studio.Services;
using Toybox.Studio.Widgets.PropertyGrid;

namespace Toybox.Studio.Widgets.EntityInspector;

/// <summary>
/// One expandable component on the selected entity, rendered as a type-driven property grid.
/// </summary>
public sealed class ComponentGroupViewModel
{
    public ComponentGroupViewModel(WorldComponent component, Action? commit)
    {
        Name = component.Name;
        Properties = [];
        foreach (var node in component.Properties)
            Properties.Add(PropertyViewModelFactory.Create(node, commit));
    }

    public string Name { get; }

    public ObservableCollection<PropertyViewModelBase> Properties { get; }
}
