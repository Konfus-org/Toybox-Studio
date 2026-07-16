namespace Toybox.Studio.Utils.Attributes;

/// <summary>
/// Groups a reflected property under a named heading in a reflective UI — the property grid gathers the
/// top-level properties that share a category into one collapsible card, in the categories' first-appearance
/// order, with the uncategorized properties floating above them in a header-less group. A nested property
/// (inside a composite or list) ignores its category; grouping is a top-level concern. Lives in Utils so
/// plain data layers can tag members without referencing any UI project.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CategoryAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
