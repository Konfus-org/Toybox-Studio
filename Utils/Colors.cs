using System;
using Avalonia.Media;

namespace Toybox.Studio.Utils;

/// <summary>
/// The editor's branded named colours (mirrors <c>tbx/types/color.h</c>'s constants) as strongly-typed Avalonia
/// <see cref="Color"/> consts — the single source code uses for an icon/accent colour (e.g.
/// <c>IconColor = Colors.Red</c>, a context-menu row's <c>.Color(Colors.Red)</c>). Where a real <see cref="Color"/>
/// can't go — a C# attribute argument or persisted metadata — a <see cref="PaletteColor"/> token names the colour
/// instead; <see cref="ToColor"/> resolves it back to the matching const, so the tokens and the consts never
/// drift. There is no string form: an unknown colour is a compile error, not a mistyped name.
/// </summary>
public static class Colors
{
    public static readonly Color White = Color.Parse("#FFFFFF");
    public static readonly Color Black = Color.Parse("#000000");
    public static readonly Color Red = Color.Parse("#E24B4A");
    public static readonly Color Green = Color.Parse("#639922");
    public static readonly Color Blue = Color.Parse("#378ADD");
    public static readonly Color Yellow = Color.Parse("#E8B53A");
    public static readonly Color Cyan = Color.Parse("#1D9E75");
    public static readonly Color Magenta = Color.Parse("#D4537E");
    public static readonly Color Grey = Color.Parse("#888780");
    public static readonly Color LightGrey = Color.Parse("#B4B2A9");
    public static readonly Color DarkGrey = Color.Parse("#5F5E5A");

    /// <summary>The palette colour for a <see cref="PaletteColor"/> token, or null for
    /// <see cref="PaletteColor.None"/> (the caller then inherits the surrounding ink).</summary>
    public static Color? ToColor(this PaletteColor color) => color switch
    {
        PaletteColor.White => White,
        PaletteColor.Black => Black,
        PaletteColor.Red => Red,
        PaletteColor.Green => Green,
        PaletteColor.Blue => Blue,
        PaletteColor.Yellow => Yellow,
        PaletteColor.Cyan => Cyan,
        PaletteColor.Magenta => Magenta,
        PaletteColor.Grey => Grey,
        PaletteColor.LightGrey => LightGrey,
        PaletteColor.DarkGrey => DarkGrey,
        _ => null,
    };
}
