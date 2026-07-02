using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Extensions;

namespace Toybox.Studio.Themes;

/// <summary>
/// Applies a <see cref="Theme"/> to the live Avalonia resources. It computes the WCAG auto-contrasted inks once
/// (into a <see cref="ThemeInks"/>) and hands them to the focused appliers that own each concern:
/// <see cref="ThemeInkApplier"/> (the published colours + core brushes + accent ramp), <see cref="FluentBrushApplier"/>
/// (the FluentTheme control-surface overrides), <see cref="DockBrushApplier"/> (the Dock.Avalonia chrome), and
/// <see cref="ThemeTokenApplier"/> (radii, fonts, clay shadows). There is no light/dark variant — the Avalonia base
/// variant is derived from the theme's Background so FluentTheme renders its controls correctly.
///
/// What it deliberately does NOT publish: the app's own translucent neutral brushes (borders, bands, wells,
/// scrims, grooves, muted/on-colour text …). Those are declared once in AppStyles.axaml as
/// <c>SolidColorBrush</c>es deriving from the small set of <c>Theme*Color</c> resources <see cref="ThemeInkApplier"/>
/// publishes. The Fluent/Dock keys stay in code because overriding a third-party theme's keys needs
/// Application.Resources precedence.
/// </summary>
public sealed class ThemeApplier
{
    /// <summary>Raised after a theme is applied so dependents (e.g. the engine log colors) can re-sync.</summary>
    public event Action? ThemeChanged;

    /// <summary>The theme last applied to the live resources.</summary>
    public Theme Active { get; private set; } = Theme.DefaultClay();

    /// <summary>
    /// Writes every theme token onto the live Avalonia resource dictionary. When <paramref name="notify"/>
    /// is true (the default) it also raises <see cref="ThemeChanged"/>; live preview passes false to avoid
    /// spamming that work on every edit.
    /// </summary>
    public void Apply(Theme theme, bool notify = true)
    {
        Active = theme;
        if (Application.Current is not { } app)
            return;

        var colors = theme.Colors;
        app.RequestedThemeVariant = IsLight(colors.Background) ? ThemeVariant.Light : ThemeVariant.Dark;

        // AUTO-CONTRAST INKS: the theme provides a BASE text colour; we push it lighter or darker (whichever
        // moves away from the background) until it clears a WCAG-style contrast ratio, so text is always
        // legible. On-colour ink does the same against the PRIMARY fill; muted ink sits at a lower floor so it
        // reads as secondary without becoming illegible. Computed once here and shared via the palette.
        var background = colors.Background.Representative;
        var text = Contrast.Ensure(colors.Text.Start, background, 8.5);
        // ON-PRIMARY ink must be legible on the PRIMARY fill, so contrast it AGAINST the primary — not against
        // itself (passing the same colour as base and background yields Ratio==1, which never adjusts). Seed the
        // ink from the primary's luminance — near-black on a light primary, white on a dark one — so even a
        // mid-luminance primary (e.g. a muted teal) starts on the legible side and Ensure only has to refine it.
        var primaryFill = colors.Primary.Representative;
        var onPrimarySeed = Contrast.RelativeLuminance(primaryFill) > 0.5 ? Color.FromRgb(0x14, 0x14, 0x14) : Colors.White;
        var onPrimary = Contrast.Ensure(onPrimarySeed, primaryFill, 4.5);
        var muted = Contrast.Ensure(text.Blend(background, 0.40f), background, 4.5);

        var palette = new ThemeInks(theme, background, text, onPrimary, muted, colors.Primary.Start);
        var resources = app.Resources;

        ThemeInkApplier.Apply(resources, palette);
        FluentBrushApplier.Apply(resources, palette);
        DockBrushApplier.Apply(resources, palette);
        ThemeTokenApplier.Apply(resources, palette);

        if (notify)
            ThemeChanged?.Invoke();
    }

    /// <summary>
    /// Whether a background reads as light, used to pick the Avalonia base variant (so FluentTheme's own
    /// control colours match). Uses the gradient's representative (midpoint) colour and perceived luminance.
    /// </summary>
    private static bool IsLight(ColorGradient background)
    {
        var c = background.Representative;
        var luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        return luminance > 0.5;
    }
}
