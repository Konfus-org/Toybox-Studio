namespace Toybox.Studio.Utils.Attributes;

/// <summary>
/// Renders a numeric property as a bounded slider (<paramref name="minimum"/>..<paramref name="maximum"/>)
/// in reflective UIs instead of the default spinner — for values that live on a natural scale (an
/// intensity dial from 0 to 1). The property grid's reflection factory honors it on any numeric leaf; a
/// non-numeric property ignores it. Lives in Utils so plain data layers can tag members without
/// referencing any UI project.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SliderAttribute(double minimum, double maximum) : Attribute
{
    public double Minimum { get; } = minimum;

    public double Maximum { get; } = maximum;
}
