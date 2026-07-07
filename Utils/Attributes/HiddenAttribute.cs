namespace Toybox.Studio.Utils.Attributes;

/// <summary>
/// Marks a property as hidden from reflective UIs — the property grid's reflection factory skips it, so
/// bookkeeping data (recent-project lists, last-opened paths) never renders as an editable row. Lives in
/// Utils so plain data layers can tag members without referencing any UI project.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class HiddenAttribute : Attribute;
