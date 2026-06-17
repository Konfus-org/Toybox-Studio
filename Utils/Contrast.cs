using System;
using Avalonia.Media;

namespace Toybox.Studio.Utils;

/// <summary>
/// WCAG-style colour contrast helpers. Shared by the theme engine (auto-contrasting text and on-colour ink
/// against the surface) and <c>IconView</c> (so an icon's colour picks the same darker/lighter adjustment as
/// text), keeping one source of truth for the maths.
/// </summary>
public static class Contrast
{
    /// <summary>
    /// Nudges <paramref name="baseColor"/> away from <paramref name="background"/> — darker over a light
    /// background, lighter over a dark one — in small steps until their WCAG contrast ratio reaches
    /// <paramref name="minRatio"/>, or it tops/bottoms out. Lets any base colour stay legible on any surface.
    /// </summary>
    public static Color Ensure(Color baseColor, Color background, double minRatio)
    {
        var toward = RelativeLuminance(background) > 0.5 ? Colors.Black : Colors.White;
        var c = baseColor;
        for (var i = 0; i < 40 && Ratio(c, background) < minRatio; i++)
            c = Blend(c, toward, 0.05f);
        return c;
    }

    /// <summary>WCAG contrast ratio between two colours (1:1 … 21:1).</summary>
    public static double Ratio(Color a, Color b)
    {
        double la = RelativeLuminance(a), lb = RelativeLuminance(b);
        var (hi, lo) = la > lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>WCAG relative luminance of an sRGB colour (0 … 1).</summary>
    public static double RelativeLuminance(Color c) =>
        0.2126 * Linearize(c.R) + 0.7152 * Linearize(c.G) + 0.0722 * Linearize(c.B);

    /// <summary>Linear interpolation between two colours, keeping <paramref name="a"/>'s alpha.</summary>
    public static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        a.A,
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private static double Linearize(byte channel)
    {
        var s = channel / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
