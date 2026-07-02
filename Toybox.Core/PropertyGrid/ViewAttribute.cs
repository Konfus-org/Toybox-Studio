namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Names a custom editor control for a reflected property or field — the C# counterpart of the engine's
/// [[editor::view]]. When the property grid is built by reflecting a POCO or an engine-synced component, a
/// tagged member routes to the registered widget of that name; data below the property-grid layer uses this
/// (a string) rather than the [ViewModel(typeof)] attribute (which references an editor type). With no
/// attribute the grid falls back to the member's type.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ViewAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
