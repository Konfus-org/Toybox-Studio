using System;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The display order of a reflected member within its grid group (ascending) — the C# counterpart of the engine's
/// declaration-order <c>order</c> attribute. Members without it sort after, keeping declaration order otherwise.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class OrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}
