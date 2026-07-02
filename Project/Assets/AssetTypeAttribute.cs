using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Project.Assets;

/// <summary>
/// The create-chooser presentation of one member of an asset-type enum (a <see cref="MaterialType"/> render
/// category, a <see cref="ShaderType"/> pipeline stage): a human <see cref="Label"/> and <see cref="Description"/>,
/// an <see cref="Icon"/> badge and its accent <see cref="Color"/> (a strongly-typed <see cref="PaletteColor"/>
/// token, since an <see cref="Avalonia.Media.Color"/> can't be an attribute argument), and — for a kind keyed by
/// file extension (shaders) — the <see cref="Extension"/> scaffolded for it. The enum stays the single source of
/// truth: the "New …" menu builds its rows from these attributes rather than a parallel hand-kept list.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class AssetTypeAttribute(
    string label, string description, Icon icon, PaletteColor color = PaletteColor.None) : Attribute
{
    public string Label { get; } = label;

    public string Description { get; } = description;

    public Icon Icon { get; } = icon;

    public PaletteColor Color { get; } = color;

    /// <summary>The source-file extension (no dot, e.g. <c>"vert"</c>) scaffolded for this type, for a kind keyed
    /// by extension (shaders); empty for a type carried in the asset body instead (a material's render category).</summary>
    public string Extension { get; init; } = "";

    /// <summary>The attribute on one enum member. Every member of an asset-type enum must declare one, so a
    /// missing attribute is a programming error and throws rather than silently dropping the member.</summary>
    public static AssetTypeAttribute Of<TEnum>(TEnum value) where TEnum : struct, Enum =>
        typeof(TEnum).GetField(value.ToString())?.GetCustomAttribute<AssetTypeAttribute>()
        ?? throw new InvalidOperationException($"{typeof(TEnum).Name}.{value} has no [AssetType].");

    /// <summary>Every member of the enum paired with its presentation, in declaration order — what the create
    /// chooser enumerates.</summary>
    public static IReadOnlyList<(TEnum Value, AssetTypeAttribute Info)> Options<TEnum>() where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(value => (value, Of(value))).ToList();

    /// <summary>The enum member whose <see cref="Extension"/> matches <paramref name="extension"/>, else
    /// <paramref name="fallback"/> — how an extension-keyed asset (a shader) recovers its type from its file.</summary>
    public static TEnum ByExtension<TEnum>(string extension, TEnum fallback) where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>()
            .FirstOrDefault(value => string.Equals(Of(value).Extension, extension, StringComparison.OrdinalIgnoreCase),
                fallback);
}
