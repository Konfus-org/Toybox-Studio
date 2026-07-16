namespace Toybox.Studio.Utils.Attributes;

/// <summary>
/// A short explanatory tooltip for a member, surfaced by any UI that opts into showing it (today the
/// property grid's enum dropdown shows it per option — see
/// <see cref="Extensions.EnumExtensions.GetTooltip(System.Enum)"/>). Members without one simply have no
/// tooltip.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TooltipAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}
