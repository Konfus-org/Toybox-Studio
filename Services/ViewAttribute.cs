namespace Toybox.Studio.Services;

/// <summary>
/// Names a custom editor control for a settings POCO property — the C# counterpart of the engine's
/// [[editor::view]]. When the property grid is built by reflecting a POCO, a tagged property is
/// rewritten as a typed wrapper carrying <c>$view</c>, which the property-view registry resolves to a
/// custom widget. With no attribute the grid falls back to the property's type.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ViewAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
