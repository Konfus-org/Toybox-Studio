namespace Toybox.Studio.Utils.Attributes;

/// <summary>
/// Overrides the label a UI shows for an enum member, for labels the member name can't spell (like
/// "Don't Save"). Read by <see cref="Extensions.EnumExtensions.GetDisplayName{TEnum}"/>; members
/// without one fall back to the humanized member name.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class DisplayNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
