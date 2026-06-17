using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Toybox.Studio.Models;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.Theming;

/// <summary>
/// Applies a <see cref="Theme"/> to the live Avalonia resources: gradients, fonts, rounding, shadows, the
/// auto-contrasted text/on-colour inks, and the third-party (FluentTheme + Dock.Avalonia) brush keys the app
/// overrides. There is no light/dark variant — the Avalonia base variant is derived from the theme's
/// Background so FluentTheme renders its controls correctly.
///
/// What it deliberately does NOT publish: the app's own translucent neutral brushes (borders, bands, wells,
/// scrims, grooves, muted/on-colour text …). Those are declared once in AppStyles.axaml as
/// <c>SolidColorBrush</c>es deriving from the small set of <c>Theme*Color</c> resources this class publishes,
/// so the derivation reads in one declarative place instead of being recomputed here. The Fluent/Dock keys
/// stay in code because overriding a third-party theme's keys needs Application.Resources precedence.
/// </summary>
public sealed class ThemeApplier
{
    /// <summary>The theme last applied to the live resources.</summary>
    public Theme Active { get; private set; } = Theme.DefaultClay();

    /// <summary>Raised after a theme is applied so dependents (e.g. the engine log colors) can re-sync.</summary>
    public event Action? ThemeChanged;

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

        var resources = app.Resources;

        // AUTO-CONTRAST INKS: the theme provides a BASE text colour; we push it lighter or darker (whichever
        // moves away from the background) until it clears a WCAG-style contrast ratio, so text is always
        // legible. On-colour ink does the same against the PRIMARY fill; muted ink sits at a lower floor so it
        // reads as secondary without becoming illegible.
        var background = colors.Background.Representative;
        var text = Contrast.Ensure(colors.Text.Start, background, 8.5);
        var onPrimary = Contrast.Ensure(colors.Primary.Representative, colors.Primary.Representative, 8.5);
        var muted = Contrast.Ensure(ColorMath.Blend(text, background, 0.40f), background, 4.5);

        // The compact set of COLOURS that AppStyles derives its translucent neutral brushes from (border,
        // header, band, well, scrim, groove, muted/on-colour text, …). Publishing the colours — not the
        // brushes — is what lets the brush derivations live declaratively in XAML.
        resources["ThemeTextColor"] = text;
        resources["ThemeOnColorColor"] = onPrimary;
        resources["ThemeMutedColor"] = muted;
        resources["ThemeBackgroundColor"] = background;
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
        var accent = colors.Primary.Start;
        resources["SystemAccentColor"] = accent;
        resources["SystemAccentColorLight1"] = ColorMath.Blend(accent, Colors.White, 0.3f);
        resources["SystemAccentColorLight2"] = ColorMath.Blend(accent, Colors.White, 0.5f);
        resources["SystemAccentColorLight3"] = ColorMath.Blend(accent, Colors.White, 0.7f);
        resources["SystemAccentColorDark1"] = ColorMath.Blend(accent, Colors.Black, 0.2f);
        resources["SystemAccentColorDark2"] = ColorMath.Blend(accent, Colors.Black, 0.4f);
        resources["SystemAccentColorDark3"] = ColorMath.Blend(accent, Colors.Black, 0.6f);

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
            "AccentButtonBackgroundPointerOver",
            "AccentButtonBackgroundPressed",
        })
            resources[key] = primaryBrush;

        // Dock.Avalonia ships its own #007ACC accent + system-derived chrome; point its overridable brush
        // keys at the active theme so docking (selected tabs, the drop-position highlight, the drag overlay
        // backdrop, and panel chrome) tracks the theme's colours. These override a third-party theme, so they
        // stay in code (Application.Resources precedence) rather than moving to AppStyles. The drop-target
        // selector squares are baked white PNGs in Dock — DockSelectorTinter multiply-tints them at runtime
        // and AppStyles points the selector Images at the recoloured bitmaps.
        var dockBg = background;
        var primaryStart = colors.Primary.Start;
        var primaryEnd = colors.Primary.End;

        // VERIFIED against Dock's Fluent/Accents/Fluent.axaml + ToolChromeControl.axaml (the real keys):
        //  • the ACTIVE panel header (Grid#PART_Grip in the :active state) fills with DockSurfaceHeaderActiveBrush
        //  • inactive header fills with DockSurfacePanelBrush
        //  • the panel OUTLINE (Border#PART_Border) uses DockBorderSubtleBrush / DockBorderStrongBrush
        var primaryBrushForDock = colors.Primary.ToBrush();
        // Panel surface — a gentle warm vertical ramp. The top stop is warmed (NOT the near-white surface
        // start) so the panel's top edge doesn't read as a bright white border against the window.
        var dockSurface = ColorGradient.BuildBrush(
            ColorMath.Blend(colors.Surface.Start, colors.Surface.End, 0.5f), colors.Surface.End, 90);

        // Active title header = the SAME primary gradient as the action buttons; inactive header = surface.
        resources["DockSurfaceHeaderActiveBrush"] = primaryBrushForDock;
        resources["DockSurfaceHeaderBrush"] = dockSurface;
        resources["DockSurfacePanelBrush"] = dockSurface;
        resources["DockApplicationAccentForegroundBrush"] = new SolidColorBrush(onPrimary);
        resources["DockThemeAccentBrush"] = primaryBrushForDock;
        resources["DockApplicationAccentBrushLow"] = primaryBrushForDock;
        resources["DockApplicationAccentBrushMed"] = TitleGradient(primaryStart, primaryEnd, 0.15f);
        resources["DockApplicationAccentBrushHigh"] = TitleGradient(primaryStart, primaryEnd, 0.30f);
        resources["DockApplicationAccentBrushIndicator"] = primaryBrushForDock;
        resources["DockTargetIndicatorBrush"] = new SolidColorBrush(ColorMath.WithAlpha(accent, 0x80));

        // Each docking panel surface (a gentle vertical ramp; RelativeUnit re-maps per panel).
        resources["DockThemeBackgroundBrush"] = dockSurface;
        resources["DockThemeControlBackgroundBrush"] = dockSurface;
        resources["DockSurfaceWorkbenchBrush"] = dockSurface;
        resources["DockSurfaceSidebarBrush"] = dockSurface;
        resources["DockSurfaceEditorBrush"] = dockSurface;
        resources["DockThemeForegroundBrush"] = new SolidColorBrush(text);

        // NO panel outlines — every Dock border brush goes transparent (the soft shadow separates cards).
        foreach (var key in new[]
        {
            "DockThemeBorderLowBrush", "DockBorderSubtleBrush", "DockBorderStrongBrush",
            "DockSeparatorBrush", "DockDocumentContentBorderBrush",
        })
            resources[key] = new SolidColorBrush(Colors.Transparent);

        resources["DockSelectorOverlayBackdropBrush"] = new SolidColorBrush(ColorMath.WithAlpha(dockBg, 0xC0));

        // Chrome buttons (pin / menu / close): themed icon + a faint hover band, no fill of their own.
        resources["DockToolChromeIconBrush"] = new SolidColorBrush(text);
        resources["DockChromeButtonForegroundBrush"] = new SolidColorBrush(text);
        // No drag-grip bar in the title bars — the whole header is draggable anyway.
        resources["DockChromeGripBrush"] = new SolidColorBrush(Colors.Transparent);
        resources["DockChromeButtonHoverBackgroundBrush"] = new SolidColorBrush(ColorMath.WithAlpha(text, 0x18));
        resources["DockChromeButtonPressedBackgroundBrush"] = new SolidColorBrush(ColorMath.WithAlpha(text, 0x28));

        // Tabs (tool + document): clay feel — transparent idle, faint hover, the surface gradient behind the
        // ACTIVE tab with an accent indicator + accent text.
        resources["DockTabBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
        resources["DockDocumentTabStripBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
        resources["DockTabHoverBackgroundBrush"] = new SolidColorBrush(ColorMath.WithAlpha(text, 0x14));
        // Active tab has a PURPLE (primary) background, so its text/indicator use the on-primary ink. Inactive
        // tab text is the auto-contrast muted ink (on the cream strip).
        resources["DockTabActiveBackgroundBrush"] = primaryBrushForDock;
        resources["DockTabActiveIndicatorBrush"] = primaryBrushForDock;
        var onPrimaryBrush = new SolidColorBrush(onPrimary);
        var darkInk = new SolidColorBrush(text);
        resources["DockTabForegroundBrush"] = new SolidColorBrush(Contrast.Ensure(ColorMath.Blend(text, background, 0.35f), background, 4.5));
        resources["DockTabSelectedForegroundBrush"] = onPrimaryBrush;
        resources["DockTabActiveForegroundBrush"] = onPrimaryBrush;
        resources["DockDocumentTabSelectedForegroundBrush"] = onPrimaryBrush;
        resources["DockDocumentTabPointerOverForegroundBrush"] = darkInk;

        // Targeted recolour of Dock's white drop-target selector PNGs: the warm/yellow dock-edge highlight
        // becomes the primary accent; the neutral window glyph is tinted between Text and Surface by its
        // lightness, so it reads dark-on-light or light-on-dark automatically.
        DockSelectorTinter.Tint(resources, accent, text, colors.Surface.Representative);

        // FLAT control surfaces: text inputs are near-white pills (with the inset well shadow they read as the
        // clean white fields in the reference). These are FluentTheme keys, so they stay in code.
        var flatBorder = new SolidColorBrush(ColorMath.WithAlpha(text, 0x22));
        var transparent = new SolidColorBrush(Colors.Transparent);
        var inputFill = new SolidColorBrush(colors.Surface.Start);
        var inputHover = new SolidColorBrush(ColorMath.Blend(colors.Surface.Start, Colors.White, 0.4f));
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundFocused" })
            resources[key] = inputFill;
        resources["TextControlBackgroundPointerOver"] = inputHover;
        resources["TextControlBorderBrush"] = flatBorder;
        resources["TextControlBorderBrushPointerOver"] = flatBorder;
        resources["TextControlBorderBrushFocused"] = new SolidColorBrush(accent);

        // Buttons: a soft top→bottom clay gradient (lighter top, slightly darker bottom) so each reads as a
        // raised pill rather than a flat block; hover brightens and press recesses, both keeping the gradient.
        resources["ButtonBackground"] = ButtonGradient(colors.Surface, 0.32f, 0.10f);
        resources["ButtonBackgroundPointerOver"] = ButtonGradient(colors.Surface, 0.48f, 0.04f);
        resources["ButtonBackgroundPressed"] = ButtonGradient(colors.Surface, 0.16f, 0.16f);
        resources["ButtonBorderBrush"] = transparent;
        resources["ButtonBorderBrushPointerOver"] = transparent;
        resources["ButtonBorderBrushPressed"] = transparent;

        // Consistent disabled chrome across ALL buttons — a muted surface fill + muted ink derived from the
        // theme. The fill is our own key (a blend, kept here); the *ForegroundDisabled keys are FluentTheme's.
        var disabledFill = new SolidColorBrush(ColorMath.Blend(colors.Surface.Representative, background, 0.5f));
        var disabledInk = new SolidColorBrush(ColorMath.WithAlpha(text, 0x59));
        foreach (var key in new[] { "ButtonBackgroundDisabled", "ComboBoxBackgroundDisabled", "TextControlBackgroundDisabled" })
            resources[key] = disabledFill;
        foreach (var key in new[] { "ButtonForegroundDisabled", "ComboBoxForegroundDisabled", "TextControlForegroundDisabled" })
            resources[key] = disabledInk;
        resources["ThemeDisabledBrush"] = disabledFill;

        // Semantic button colours, all theme-driven and using the SAME top→bottom gradient shading as the
        // standard button: action = primary (brand), play = success (green), stop = error (red).
        SetColorButton(resources, "ThemeActionButton", colors.Primary.Start, colors.Primary.End);
        SetColorButton(resources, "ThemePlayButton", colors.Success.Representative, colors.Success.Representative);
        SetColorButton(resources, "ThemeStopButton", colors.Error.Representative, colors.Error.Representative);

        // Combo boxes: flat near-white pills like the text inputs (no gradient).
        resources["ComboBoxBackground"] = inputFill;
        resources["ComboBoxBackgroundPointerOver"] = inputHover;
        resources["ComboBoxBackgroundPressed"] = new SolidColorBrush(ColorMath.Blend(colors.Surface.Start, Colors.Black, 0.06f));
        resources["ComboBoxBackgroundFocused"] = inputFill;

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
                OffsetX = ox * 2, OffsetY = oy * 2, Blur = 11, Spread = -1, Color = shadowColor,
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
        // set above (Surface.Start, NOT the representative midpoint), so it stays in code.
        resources["ThemeWellFillBrush"] = inputFill;

        // A brightened flat accent for hover affordances (e.g. the reset indicator brightening on hover) —
        // a blend toward white, so it stays in code rather than moving to an opacity-only XAML derivation.
        resources["ThemeAccentBrightBrush"] = new SolidColorBrush(ColorMath.Blend(colors.Primary.Representative, Colors.White, 0.28f));

        // Tab + panel rounding derived from the theme's CornerRadius: a tab rounds only its TOP corners while
        // the panel rounds only its BOTTOM corners, so the selected tab reads as part of the panel it owns.
        resources["ThemeTabCornerRadius"] = new CornerRadius(theme.CornerRadius, theme.CornerRadius, 0, 0);
        // The tabbed content panel is SQUARE: a rounded bottom would expose the (warmer) surface behind its
        // corners, reading as a shadow over the rounding.
        resources["ThemePanelCornerRadius"] = new CornerRadius(0);

        if (notify)
            ThemeChanged?.Invoke();
    }

    /// <summary>A vertical (top→bottom) clay gradient for buttons, lightened at the top and darkened at the
    /// bottom relative to the surface stops, so the button reads as a raised, moulded pill.</summary>
    private static IBrush ButtonGradient(ColorGradient surface, float lightenTop, float darkenBottom) =>
        ColorGradient.BuildBrush(
            ColorMath.Blend(surface.Start, Colors.White, lightenTop),
            ColorMath.Blend(surface.End, Colors.Black, darkenBottom),
            90);

    /// <summary>
    /// Writes a coloured semantic button as a top→bottom gradient (light top, slightly darker bottom) plus a
    /// brightened hover variant under <paramref name="key"/> + "Brush" / + "HoverBrush". A single-colour
    /// button (top == bottom) still gets the lighten/darken split; hover brightens both stops toward white.
    /// </summary>
    private static void SetColorButton(IResourceDictionary resources, string key, Color top, Color bottom)
    {
        var topStop = ColorMath.Blend(top, Colors.White, 0.16f);
        var bottomStop = ColorMath.Blend(bottom, Colors.Black, 0.14f);
        resources[key + "Brush"] = ColorGradient.BuildBrush(topStop, bottomStop, 90);
        resources[key + "HoverBrush"] = ColorGradient.BuildBrush(
            ColorMath.Blend(topStop, Colors.White, 0.15f), ColorMath.Blend(bottomStop, Colors.White, 0.15f), 90);
    }

    /// <summary>
    /// A left→right (angle 0) linear gradient between two primary stops, lightened toward white — used for the
    /// dock title bars and their hover/active variants.
    /// </summary>
    private static IBrush TitleGradient(Color start, Color end, float lighten) =>
        ColorGradient.BuildBrush(
            ColorMath.Blend(start, Colors.White, lighten),
            ColorMath.Blend(end, Colors.White, lighten),
            0);

    private static void SetBrush(IResourceDictionary resources, string key, ColorGradient gradient) =>
        resources[key] = gradient.ToBrush();

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
