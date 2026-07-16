using Avalonia.Controls;
using Avalonia.Media;
using Toybox.Studio.Utils.Extensions;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Themes;

/// <summary>
/// Overrides the FluentTheme brush keys the app re-skins to the active theme: the accent-fill surfaces, the flat
/// clay text inputs and combo boxes, the gradient buttons (standard + semantic), the menu/flyout panels, and the
/// sliders. These override a third-party theme, so they stay in code (Application.Resources precedence) rather
/// than moving to AppStyles.
/// </summary>
internal static class FluentBrushApplier
{
    public static void Apply(IResourceDictionary resources, in ThemeInks p)
    {
        var colors = p.Theme.Colors;
        var accent = p.Accent;
        var background = p.Background;
        var text = p.Text;

        // Where FluentTheme fills an accent SURFACE (selected list/tree/tab items, accent buttons, the
        // toggle-switch's "on" track), point its accent-fill brushes at the primary GRADIENT so a selected
        // element's primary colour reads as a gradient rather than a flat block. These keys are consumed as
        // IBrush, so a gradient brush slots straight in; the SystemAccentColor* colours above stay flat for
        // the places that genuinely need a single colour.
        var primaryBrush = colors.Primary.ToBrush();
        foreach (var key in new[]
        {
            "SystemAccentColorBrush",
            "SystemControlBackgroundAccentBrush",
            "SystemControlHighlightAccentBrush",
            "AccentFillColorDefaultBrush",
            "AccentFillColorSecondaryBrush",
            "AccentFillColorTertiaryBrush",
            "AccentButtonBackground",
        })
            resources[key] = primaryBrush;

        // The accent button (Fluent's Classes="accent") must REACT like our other buttons: hover brightens,
        // press recesses — otherwise it sits inert (the "primary button doesn't fade on hover" bug). Build the
        // hover/press fills from the primary stops, same lighten/darken split as SetColorButton. (Our own
        // primary buttons use Classes="action"; this keeps any stray accent button consistent too.)
        resources["AccentButtonBackgroundPointerOver"] = ColorGradient.BuildBrush(
            colors.Primary.Start.Blend(Colors.White, 0.15f),
            colors.Primary.End.Blend(Colors.White, 0.15f), 90);
        resources["AccentButtonBackgroundPressed"] = ColorGradient.BuildBrush(
            colors.Primary.Start.Blend(Colors.Black, 0.10f),
            colors.Primary.End.Blend(Colors.Black, 0.14f), 90);

        // FLAT control surfaces: text inputs are near-white pills (with the inset well shadow they read as the
        // clean white fields in the reference). These are FluentTheme keys, so they stay in code.
        var transparent = new SolidColorBrush(Colors.Transparent);
        var inputFill = new SolidColorBrush(colors.Surface.Start);
        var inputHover = new SolidColorBrush(colors.Surface.Start.Blend(Colors.White, 0.4f));
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundFocused" })
            resources[key] = inputFill;
        resources["TextControlBackgroundPointerOver"] = inputHover;
        // No hard outline: the inset well shadow alone defines the recess (clay "pressed-in" field). The border
        // only appears, softly, on focus as the accent affordance (and fades in via the focus brush transition).
        resources["TextControlBorderBrush"] = transparent;
        resources["TextControlBorderBrushPointerOver"] = transparent;
        resources["TextControlBorderBrushFocused"] = new SolidColorBrush(accent);

        // Buttons: a soft top→bottom clay gradient (lighter top, slightly darker bottom) so each reads as a
        // raised pill rather than a flat block; hover brightens and press recesses, both keeping the gradient.
        // The plain (un-classed) button uses the theme's DEFAULT button colour (which defaults to the surface).
        resources["ButtonBackground"] = ButtonGradient(colors.Default, 0.32f, 0.10f);
        resources["ButtonBackgroundPointerOver"] = ButtonGradient(colors.Default, 0.48f, 0.04f);
        resources["ButtonBackgroundPressed"] = ButtonGradient(colors.Default, 0.16f, 0.16f);
        resources["ButtonBorderBrush"] = transparent;
        resources["ButtonBorderBrushPointerOver"] = transparent;
        resources["ButtonBorderBrushPressed"] = transparent;

        // Consistent disabled chrome across ALL buttons — a muted surface fill + muted ink derived from the
        // theme. The fill is our own key (a blend, kept here); the *ForegroundDisabled keys are FluentTheme's.
        var disabledFill = new SolidColorBrush(colors.Surface.Representative.Blend(background, 0.5f));
        var disabledInk = new SolidColorBrush(text.WithAlpha(0x59));
        foreach (var key in new[] { "ButtonBackgroundDisabled", "ComboBoxBackgroundDisabled", "TextControlBackgroundDisabled" })
            resources[key] = disabledFill;
        foreach (var key in new[] { "ButtonForegroundDisabled", "ComboBoxForegroundDisabled", "TextControlForegroundDisabled" })
            resources[key] = disabledInk;
        resources["ThemeDisabledBrush"] = disabledFill;

        // Semantic button colours, each from its own palette entry and using the SAME top→bottom gradient
        // shading as the standard button: action (brand), play (green), stop (red), refresh (blue). Their
        // defaults mirror the brand/semantic colours, so the stock look is unchanged.
        SetColorButton(resources, "ThemeActionButton", colors.Action.Start, colors.Action.End);
        SetColorButton(resources, "ThemePlayButton", colors.Play.Start, colors.Play.End);
        SetColorButton(resources, "ThemeStopButton", colors.Stop.Start, colors.Stop.End);
        SetColorButton(resources, "ThemeRefreshButton", colors.Refresh.Start, colors.Refresh.End);

        // Combo boxes: flat near-white pills like the text inputs (no gradient), and — like the text inputs —
        // no hard outline (the inset well shadow carries the recess); only the focus border shows as accent.
        resources["ComboBoxBackground"] = inputFill;
        resources["ComboBoxBackgroundPointerOver"] = inputHover;
        resources["ComboBoxBackgroundPressed"] = new SolidColorBrush(colors.Surface.Start.Blend(Colors.Black, 0.06f));
        resources["ComboBoxBackgroundFocused"] = inputFill;
        resources["ComboBoxBorderBrush"] = transparent;
        resources["ComboBoxBorderBrushPointerOver"] = transparent;
        resources["ComboBoxBorderBrushPressed"] = transparent;
        resources["ComboBoxBorderBrushFocused"] = new SolidColorBrush(accent);

        // Menu / flyout dropdown panels: the popup surfaces (top-level menu dropdowns, submenus, context
        // menus, flyouts) default to a Fluent neutral that ignores the theme. Point them at the theme surface
        // (a flat fill the translucent item highlight tints over) with a subtle border. FluentTheme keys, so
        // they stay in code for Application.Resources precedence.
        var menuSurface = new SolidColorBrush(colors.Surface.Representative);
        var menuBorder = new SolidColorBrush(text.WithAlpha(0x2E));
        foreach (var key in new[] { "MenuFlyoutPresenterBackground", "FlyoutPresenterBackground" })
            resources[key] = menuSurface;
        foreach (var key in new[] { "MenuFlyoutPresenterBorderBrush", "FlyoutBorderThemeBrush" })
            resources[key] = menuBorder;

        // Tooltips are popup panels too (e.g. the project picker's README previews): same themed surface,
        // border and ink as the flyouts, instead of FluentTheme's stock neutral panel.
        resources["ToolTipBackground"] = menuSurface;
        resources["ToolTipBorderBrush"] = menuBorder;
        resources["ToolTipForeground"] = new SolidColorBrush(text);

        // Sliders (e.g. the Accessibility animation-intensity dial, the theme editor's angle sliders): a clay
        // groove with an accent-gradient value fill and a raised surface thumb. These recolour FluentTheme's
        // slider keys (the inset groove / thumb shadow are added in SliderStyle); they stay here for the same
        // Application.Resources-precedence reason as the other Fluent overrides.
        var grooveBrush = new SolidColorBrush(text.WithAlpha(0x22));
        var thumbBrush = new SolidColorBrush(colors.Surface.Start);
        resources["SliderTrackFill"] = grooveBrush;
        resources["SliderTrackFillPointerOver"] = grooveBrush;
        resources["SliderTrackFillPressed"] = grooveBrush;
        resources["SliderTrackValueFill"] = primaryBrush;
        resources["SliderTrackValueFillPointerOver"] = primaryBrush;
        resources["SliderTrackValueFillPressed"] = primaryBrush;
        resources["SliderThumbBackground"] = thumbBrush;
        resources["SliderThumbBackgroundPointerOver"] = new SolidColorBrush(colors.Surface.Start.Blend(Colors.White, 0.3f));
        resources["SliderThumbBackgroundPressed"] = new SolidColorBrush(colors.Surface.Start.Blend(Colors.White, 0.3f));
    }

    /// <summary>A vertical (top→bottom) clay gradient for buttons, lightened at the top and darkened at the
    /// bottom relative to the surface stops, so the button reads as a raised, moulded pill.</summary>
    private static IBrush ButtonGradient(ColorGradient surface, float lightenTop, float darkenBottom) =>
        ColorGradient.BuildBrush(
            surface.Start.Blend(Colors.White, lightenTop),
            surface.End.Blend(Colors.Black, darkenBottom),
            90);

    /// <summary>
    /// Writes a coloured semantic button as a top→bottom gradient (light top, slightly darker bottom) plus a
    /// brightened hover variant under <paramref name="key"/> + "Brush" / + "HoverBrush". A single-colour
    /// button (top == bottom) still gets the lighten/darken split; hover brightens both stops toward white.
    /// </summary>
    private static void SetColorButton(IResourceDictionary resources, string key, Color top, Color bottom)
    {
        var topStop = top.Blend(Colors.White, 0.16f);
        var bottomStop = bottom.Blend(Colors.Black, 0.14f);
        resources[key + "Brush"] = ColorGradient.BuildBrush(topStop, bottomStop, 90);
        resources[key + "HoverBrush"] = ColorGradient.BuildBrush(
            topStop.Blend(Colors.White, 0.15f), bottomStop.Blend(Colors.White, 0.15f), 90);
    }
}
