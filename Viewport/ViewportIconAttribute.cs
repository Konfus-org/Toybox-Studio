using System;
using Toybox.Studio.Utils;
using Toybox.Studio.Project;

namespace Toybox.Studio.Viewport;

/// <summary>
/// The billboard icon a reflected component shows at its entity's position in editor viewports. Like
/// <see cref="IconAttribute"/>, this is editor-defined, not engine-supplied: <paramref name="name"/> is a
/// strongly-typed Lucide icon and <paramref name="color"/> a strongly-typed <see cref="PaletteColor"/> token —
/// both compile-checked, neither a string. Inherited by subtypes (a derived component keeps its base's viewport
/// icon unless it declares its own).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class ViewportIconAttribute(Icon name, PaletteColor color = PaletteColor.None) : Attribute
{
    public Icon Name { get; } = name;

    public PaletteColor Color { get; } = color;
}
