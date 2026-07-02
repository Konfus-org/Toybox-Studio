using System;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Overrides a reflected member's grid label (the humanized member name is the default) — the C# counterpart of
/// the engine's <c>[[tbx::label]]</c>. Sits on the member (or its <see cref="EngineSyncAttribute"/> backing field).
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class DisplayNameAttribute(string label) : Attribute
{
    public string Label { get; } = label;
}
