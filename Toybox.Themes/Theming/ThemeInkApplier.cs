using Avalonia.Controls;
using Avalonia.Media;
using Toybox.Studio.Utils.Extensions;
// Disambiguate the named-colour constants used below from Toybox.Studio.Utils.Colors.
using Colors = Avalonia.Media.Colors;

namespace Toybox.Studio.Theming;

/// <summary>
/// Publishes the compact set of COLOURS that AppStyles derives its translucent neutral brushes from (the
/// auto-contrast text/on-colour/muted inks + background/surface/primary), the core semantic <c>Theme*Brush</c>
/// gradients, and the flat <c>SystemAccentColor*</c> ramp FluentTheme reads its single accent from.
/// </summary>
internal static class ThemeInkApplier
{
    public static void Apply(IResourceDictionary resources, in ThemeInks p)
    {
        var colors = p.Theme.Colors;

        // The compact set of COLOURS that AppStyles derives its translucent neutral brushes from (border,
        // header, band, well, scrim, groove, muted/on-colour text, …). Publishing the colours — not the
        // brushes — is what lets the brush derivations live declaratively in XAML.
        resources["ThemeTextColor"] = p.Text;
        resources["ThemeOnColorColor"] = p.OnPrimary;
        resources["ThemeMutedColor"] = p.Muted;
        resources["ThemeBackgroundColor"] = p.Background;
        resources["ThemeSurfaceColor"] = colors.Surface.Representative;
        resources["ThemePrimaryColor"] = colors.Primary.Representative;

        SetBrush(resources, "ThemePrimaryBrush", colors.Primary);
        SetBrush(resources, "ThemeSecondaryBrush", colors.Secondary);
        SetBrush(resources, "ThemeTertiaryBrush", colors.Tertiary);
        SetBrush(resources, "ThemeErrorBrush", colors.Error);
        SetBrush(resources, "ThemeWarningBrush", colors.Warning);
        SetBrush(resources, "ThemeInfoBrush", colors.Info);
        SetBrush(resources, "ThemeSuccessBrush", colors.Success);
        SetBrush(resources, "ThemeBackgroundBrush", colors.Background);
        SetBrush(resources, "ThemeSurfaceBrush", colors.Surface);

        // FluentTheme derives control accents from a single colour; the primary gradient's start stop is
        // its dominant colour, so it stands in as the accent.
        var accent = p.Accent;
        resources["SystemAccentColor"] = accent;
        resources["SystemAccentColorLight1"] = accent.Blend(Colors.White, 0.3f);
        resources["SystemAccentColorLight2"] = accent.Blend(Colors.White, 0.5f);
        resources["SystemAccentColorLight3"] = accent.Blend(Colors.White, 0.7f);
        resources["SystemAccentColorDark1"] = accent.Blend(Colors.Black, 0.2f);
        resources["SystemAccentColorDark2"] = accent.Blend(Colors.Black, 0.4f);
        resources["SystemAccentColorDark3"] = accent.Blend(Colors.Black, 0.6f);
    }

    private static void SetBrush(IResourceDictionary resources, string key, ColorGradient gradient) =>
        resources[key] = gradient.ToBrush();
}
