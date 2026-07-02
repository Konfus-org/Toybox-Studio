namespace Toybox.Studio.Utils;

/// <summary>
/// The editor's branded palette as a strongly-typed token, one member per <see cref="Colors"/> const. It exists
/// for the places a real <see cref="Avalonia.Media.Color"/> can't go: a C# attribute argument (<c>[Icon]</c>,
/// <c>[ViewportIcon]</c>, <c>[Dockable]</c>) and persisted metadata (<c>AssetCategory</c>, <c>ToolbarItem</c>),
/// which must be compile-time constants. Resolve one to its colour with <c>Colors.ToColor</c>; everywhere a
/// <see cref="Avalonia.Media.Color"/> is usable directly, use the <see cref="Colors"/> consts instead. Because
/// the token is an enum, an unknown colour is a compile error — never a mistyped string.
/// </summary>
public enum PaletteColor
{
    /// <summary>No colour — the consumer inherits the surrounding ink.</summary>
    None = 0,
    White,
    Black,
    Red,
    Green,
    Blue,
    Yellow,
    Cyan,
    Magenta,
    Grey,
    LightGrey,
    DarkGrey,
}
