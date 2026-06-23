using Avalonia.Media;

namespace Toybox.Studio.Utils.Extensions;

/// <summary>
/// Small colour-arithmetic extension helpers shared by the theme engine and the contrast maths: a linear
/// blend between two colours and an alpha override. Kept in one place so the same blend isn't reimplemented
/// in <see cref="Contrast"/> and the theme applier.
/// </summary>
public static class ColorExtensions
{
    /// <summary>Linear interpolation from <paramref name="a"/> toward <paramref name="b"/>, keeping <paramref name="a"/>'s alpha.</summary>
    public static Color Blend(this Color a, Color b, float t) => Color.FromArgb(
        a.A,
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    /// <summary>A copy of <paramref name="color"/> with its alpha replaced by <paramref name="alpha"/>.</summary>
    public static Color WithAlpha(this Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);
}
