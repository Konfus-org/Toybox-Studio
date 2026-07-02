using System;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Shows a reflected member in the grid but disables editing — the C# counterpart of the engine's
/// <c>[[tbx::readonly]]</c>. Independent of sync: a member can be read-only for display yet still carry an
/// <see cref="EngineSyncAttribute"/> (e.g. engine-owned identity). The builder treats it as non-editable.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ReadOnlyAttribute : Attribute;
