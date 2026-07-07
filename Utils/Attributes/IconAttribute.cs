namespace Toybox.Studio.Utils.Attributes;

/// <summary>
/// Names the Lucide icon a reflective UI shows for a type or property — the property grid renders it on
/// the section header the member becomes. The name is the icon-pack kind's member name (e.g. "Hammer",
/// "FolderOpen"); an unknown name simply renders no icon. A string here (rather than the icon enum) keeps
/// Utils free of UI package references.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public sealed class IconAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
