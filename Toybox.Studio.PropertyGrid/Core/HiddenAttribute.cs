using System;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Drops a reflected member from the property grid entirely (it still syncs) — the C# counterpart of the engine's
/// <c>[[tbx::hidden]]</c>. E.g. a component's <c>is_enabled</c> flag, edited via the header toggle, not a row.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HiddenAttribute : Attribute;
