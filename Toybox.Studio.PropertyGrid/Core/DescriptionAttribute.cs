using System;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// A tooltip for a reflected member's grid row — the C# counterpart of the engine's <c>[[tbx::description]]</c>.
/// Sits on the member (or its <see cref="EngineSyncAttribute"/> backing field).
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class DescriptionAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}
