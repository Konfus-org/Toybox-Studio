using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Extensions;

namespace Toybox.Studio.Themes;

/// <summary>
/// Writes the theme's non-brush tokens: corner radii, fonts, and the direction-aware clay shadows / inset-well /
/// raised-card depth effects (which collapse to nothing when the theme disables shadows), plus the two flat
/// derived brushes that stay in code (the well fill and the brightened accent).
/// </summary>
internal static class ThemeTokenApplier
{
    public static void Apply(IResourceDictionary resources, in ThemeInks p)
    {
        var theme = p.Theme;
        var colors = theme.Colors;

        var radius = new CornerRadius(theme.CornerRadius);
        resources["ControlCornerRadius"] = radius;
        resources["OverlayCornerRadius"] = radius;
        // Right-edge-only variant (TR + BR rounded, left flat) for elements that sit flush against a rounded
        // well's right edge — e.g. the numeric field's decrease chevron, the rightmost spinner button.
        resources["ThemeRightCornerRadius"] = new CornerRadius(0, theme.CornerRadius, theme.CornerRadius, 0);

        resources["ThemeFontFamily"] = new FontFamily(theme.Font.Family);
        resources["ThemeMonoFontFamily"] = new FontFamily(theme.Font.Monospace);
        resources["ThemeFontSize"] = theme.Font.Size;

        // Theme-driven clay shadows: direction + on/off come from the theme. Buttons use a shape-correct
        // BoxShadow (it rides the rounded content presenter, so the shadow follows the pill); panels use a
        // matching DropShadowEffect. Both cast along ShadowAngle (screen space, Y down — 45° = light from the
        // upper-left, shadow to the lower-right). Disabled ⇒ empty shadow / a no-op zero-opacity effect.
        var rad = theme.ShadowAngle * Math.PI / 180.0;
        var ox = Math.Cos(rad);
        var oy = Math.Sin(rad);
        var shadowColor = Color.FromArgb(0x2B, 0, 0, 0);
        resources["ThemeButtonShadow"] = theme.ShadowsEnabled
            ? new BoxShadows(new BoxShadow
            {
                // Small offset + soft blur + slight negative spread so the shadow hugs the rounded pill and
                // reads as a gentle directional lift rather than a hard box.
                OffsetX = ox * 2,
                OffsetY = oy * 2,
                Blur = 11,
                Spread = -1,
                Color = shadowColor,
            })
            : default(BoxShadows);
        resources["ThemePanelShadowEffect"] = new DropShadowEffect
        {
            OffsetX = ox * 6,
            OffsetY = oy * 6,
            BlurRadius = 18,
            Color = Color.FromArgb(0x3A, 0, 0, 0),
            Opacity = theme.ShadowsEnabled ? 1 : 0,
        };

        // Clay depth tokens for the property grid & inputs. A PRESSED well casts a dark hairline on its
        // light-facing inner edge with a soft highlight opposite, so it reads as moulded INTO the surface. A
        // RAISED card is the mirror — lifted OUT of the surface. Both follow the theme's light angle and
        // collapse to nothing when shadows are off, so a flat theme stays flat.
        if (theme.ShadowsEnabled)
        {
            resources["ThemeWellInsetShadow"] = new BoxShadows(
                new BoxShadow { OffsetX = ox * 2, OffsetY = oy * 2, Blur = 4, Spread = 0, Color = Color.FromArgb(0x33, 0, 0, 0), IsInset = true },
                [new BoxShadow { OffsetX = -ox * 2, OffsetY = -oy * 2, Blur = 4, Spread = 0, Color = Color.FromArgb(0xAA, 255, 255, 255), IsInset = true }]);
            // Dark-only variant (no light highlight) for the numeric field's well: the white highlight of the
            // full inset lands on the bottom-right corner exactly where the spinner arrows sit, reading as a
            // weird halo over them — so the spinner border uses this softer, highlight-free press instead.
            resources["ThemeWellInsetSoftShadow"] = new BoxShadows(
                new BoxShadow { OffsetX = ox * 2, OffsetY = oy * 2, Blur = 4, Spread = 0, Color = Color.FromArgb(0x2E, 0, 0, 0), IsInset = true });
            resources["ThemeCardShadow"] = new BoxShadows(
                new BoxShadow { OffsetX = ox * 4, OffsetY = oy * 4, Blur = 14, Spread = -3, Color = Color.FromArgb(0x30, 0, 0, 0) },
                [new BoxShadow { OffsetX = -ox * 4, OffsetY = -oy * 4, Blur = 14, Spread = -3, Color = Color.FromArgb(0xC4, 255, 255, 255) }]);
        }
        else
        {
            resources["ThemeWellInsetShadow"] = default(BoxShadows);
            resources["ThemeWellInsetSoftShadow"] = default(BoxShadows);
            resources["ThemeCardShadow"] = default(BoxShadows);
        }

        // Flat fill for field hosts / pickers / the at-default indicator well — matches the flat text inputs
        // (Surface.Start, NOT the representative midpoint), so it stays in code.
        resources["ThemeWellFillBrush"] = new SolidColorBrush(colors.Surface.Start);

        // A brightened flat accent for hover affordances (e.g. the reset indicator brightening on hover) —
        // a blend toward white, so it stays in code rather than moving to an opacity-only XAML derivation.
        resources["ThemeAccentBrightBrush"] = new SolidColorBrush(colors.Primary.Representative.Blend(Colors.White, 0.28f));

        // Tab + panel rounding derived from the theme's CornerRadius: a tab rounds only its TOP corners while
        // the panel rounds only its BOTTOM corners, so the selected tab reads as part of the panel it owns.
        resources["ThemeTabCornerRadius"] = new CornerRadius(theme.CornerRadius, theme.CornerRadius, 0, 0);
        // The tabbed content panel is SQUARE: a rounded bottom would expose the (warmer) surface behind its
        // corners, reading as a shadow over the rounding.
        resources["ThemePanelCornerRadius"] = new CornerRadius(0);
    }
}
