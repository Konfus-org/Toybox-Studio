using System;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Groups a reflected member under a named heading in the property grid — the C# counterpart of the engine's
/// <c>[[tbx::category]]</c>. Sits on the member (or its <see cref="EngineSyncAttribute"/> backing field).
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class CategoryAttribute(string category) : Attribute
{
    public string Category { get; } = category;
}
