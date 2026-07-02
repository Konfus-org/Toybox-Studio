using System;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Project;

/// <summary>
/// The header-badge icon for a reflected type (component or asset), shown in the inspector. Icons are defined
/// here, in the editor, not by the engine: <paramref name="name"/> is a strongly-typed Lucide icon and
/// <paramref name="color"/> a strongly-typed <see cref="PaletteColor"/> token — both compile-checked, neither a
/// string. Inherited by subtypes (a derived component keeps its base's icon unless it declares its own).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class IconAttribute(Icon name, PaletteColor color = PaletteColor.None) : Attribute
{
    public Icon Name { get; } = name;

    public PaletteColor Color { get; } = color;
}
