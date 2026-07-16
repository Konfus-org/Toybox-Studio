using Toybox.Studio.EngineApi.Types.Components;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.Utils.Composition;
using Icon = IconPacks.Avalonia.Lucide.PackIconLucideKind;

namespace Toybox.Studio.NodeGraph;

/// <summary>One component's row in a node's Components tab: its icon and name (with an output plug, so the
/// component is referenceable) over its input plugs (its own asset/script references) and a property grid of
/// the component's fields.</summary>
public sealed class ComponentCardViewModel
{
    public ComponentCardViewModel(Component component, ViewModelFactory viewModels)
    {
        Name = component.Name;
        Icon = NodeIcons.ForComponent(component);
        References = ReferenceScanner.ComponentReferences(component);
        Grid = viewModels.Create<PropertyGridViewModel>(new ReflectionPropertyNodeFactory(viewModels));
        Grid.Show(component);
    }

    public string Name { get; }

    public Icon Icon { get; }

    /// <summary>The component's input plugs — its asset/script references, shown inline beneath its header so
    /// it is obvious which plugs belong to this component.</summary>
    public IReadOnlyList<NodeReference> References { get; }

    /// <summary>Whether the component holds any reference (gates its inline input-plug list).</summary>
    public bool HasReferences => References.Count > 0;

    public PropertyGridViewModel Grid { get; }
}
