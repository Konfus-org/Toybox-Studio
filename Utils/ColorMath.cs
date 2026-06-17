using Avalonia.Media;

namespace Toybox.Studio.Utils;

/// <summary>
/// Small colour-arithmetic helpers shared by the theme engine and the contrast maths: a linear blend
/// between two colours and an alpha override. Kept in one place so the same blend isn't reimplemented in
/// <see cref="Contrast"/> and the theme applier.
/// </summary>
public static class ColorMath
{
    /// <summary>Linear interpolation between two colours, keeping <paramref name="a"/>'s alpha.</summary>
    public static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        a.A,
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    /// <summary>A copy of <paramref name="color"/> with its alpha replaced by <paramref name="alpha"/>.</summary>
    public static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);
}
