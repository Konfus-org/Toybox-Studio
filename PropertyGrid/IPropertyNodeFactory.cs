namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Builds the property nodes a <see cref="PropertyGridViewModel"/> shows for a target — the strategy
/// that decides which of the target's properties become rows and what fills each row's slots. The
/// factory is supplied to the grid, so the one grid renders plain CLR objects through
/// <see cref="ReflectionPropertyNodeFactory"/> today and other sources (engine-mirrored objects, …)
/// through their own factories.
/// </summary>
public interface IPropertyNodeFactory
{
    IReadOnlyList<PropertyNode> CreateNodes(object target);
}
