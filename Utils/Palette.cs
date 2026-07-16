using Avalonia.Media;

namespace Toybox.Studio.Utils;

/// <summary>
/// The editor's small set of semantic accent colours, used to tint a control that carries meaning of its
/// own — a destructive menu row's red trash icon, an additive row's green plus. Named on purpose (not the
/// theme palette) so the meaning travels with the colour regardless of the active theme. Distinct from
/// <see cref="Colors"/> (Avalonia's full CSS-colour set) — reference this as <c>Palette.Red</c>.
/// </summary>
public static class Palette
{
    /// <summary>Destructive / danger (delete, remove).</summary>
    public static readonly Color Red = Color.FromRgb(0xE5, 0x48, 0x4D);

    /// <summary>Additive / create (add, new).</summary>
    public static readonly Color Green = Color.FromRgb(0x46, 0xA7, 0x58);

    /// <summary>Caution / reset-to-default.</summary>
    public static readonly Color Yellow = Color.FromRgb(0xF2, 0xC9, 0x4C);

    /// <summary>Informational accent (worlds).</summary>
    public static readonly Color Cyan = Color.FromRgb(0x29, 0xB5, 0xC9);

    /// <summary>Secondary accent (materials, shaders).</summary>
    public static readonly Color Magenta = Color.FromRgb(0xD6, 0x40, 0x9F);
}
