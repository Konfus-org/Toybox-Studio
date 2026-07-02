using Avalonia.Controls;
using Avalonia.Media;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Extensions;
// Disambiguate the named-colour constants used below from Toybox.Studio.Utils.Colors.
using Colors = Avalonia.Media.Colors;

namespace Toybox.Studio.Theming;

/// <summary>
/// Points Dock.Avalonia's overridable brush keys at the active theme so docking (panel headers, tabs, chrome
/// buttons, the drop-position highlight and drag overlay backdrop) tracks the theme's colours instead of Dock's
/// shipped #007ACC accent. These override a third-party theme, so they stay in code (Application.Resources
/// precedence). The white drop-target selector PNGs are multiply-tinted at runtime by <see cref="DockSelectorTinter"/>.
/// </summary>
internal static class DockBrushApplier
{
    public static void Apply(IResourceDictionary resources, in ThemeInks p)
    {
        var colors = p.Theme.Colors;
        var background = p.Background;
        var text = p.Text;
        var onPrimary = p.OnPrimary;
        var accent = p.Accent;

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
            colors.Surface.Start.Blend(colors.Surface.End, 0.5f), colors.Surface.End, 90);

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
        resources["DockTargetIndicatorBrush"] = new SolidColorBrush(accent.WithAlpha(0x80));

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

        resources["DockSelectorOverlayBackdropBrush"] = new SolidColorBrush(dockBg.WithAlpha(0xC0));

        // Chrome buttons (pin / menu / close): themed icon + a faint hover band, no fill of their own.
        resources["DockToolChromeIconBrush"] = new SolidColorBrush(text);
        resources["DockChromeButtonForegroundBrush"] = new SolidColorBrush(text);
        // No drag-grip bar in the title bars — the whole header is draggable anyway.
        resources["DockChromeGripBrush"] = new SolidColorBrush(Colors.Transparent);
        resources["DockChromeButtonHoverBackgroundBrush"] = new SolidColorBrush(text.WithAlpha(0x18));
        resources["DockChromeButtonPressedBackgroundBrush"] = new SolidColorBrush(text.WithAlpha(0x28));

        // Tabs (tool + document): clay feel — transparent idle, faint hover, the surface gradient behind the
        // ACTIVE tab with an accent indicator + accent text.
        resources["DockTabBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
        resources["DockDocumentTabStripBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
        resources["DockTabHoverBackgroundBrush"] = new SolidColorBrush(text.WithAlpha(0x14));
        // Active tab has a PURPLE (primary) background, so its text/indicator use the on-primary ink. Inactive
        // tab text is the auto-contrast muted ink (on the cream strip).
        resources["DockTabActiveBackgroundBrush"] = primaryBrushForDock;
        resources["DockTabActiveIndicatorBrush"] = primaryBrushForDock;
        var onPrimaryBrush = new SolidColorBrush(onPrimary);
        var darkInk = new SolidColorBrush(text);
        resources["DockTabForegroundBrush"] = new SolidColorBrush(Contrast.Ensure(text.Blend(background, 0.35f), background, 4.5));
        resources["DockTabSelectedForegroundBrush"] = onPrimaryBrush;
        resources["DockTabActiveForegroundBrush"] = onPrimaryBrush;
        resources["DockDocumentTabSelectedForegroundBrush"] = onPrimaryBrush;
        resources["DockDocumentTabPointerOverForegroundBrush"] = darkInk;

        // Targeted recolour of Dock's white drop-target selector PNGs: the warm/yellow dock-edge highlight
        // becomes the primary accent; the neutral window glyph is tinted between Text and Surface by its
        // lightness, so it reads dark-on-light or light-on-dark automatically.
        DockSelectorTinter.Tint(resources, accent, text, colors.Surface.Representative);
    }

    /// <summary>
    /// A left→right (angle 0) linear gradient between two primary stops, lightened toward white — used for the
    /// dock title bars and their hover/active variants.
    /// </summary>
    private static IBrush TitleGradient(Color start, Color end, float lighten) =>
        ColorGradient.BuildBrush(
            start.Blend(Colors.White, lighten),
            end.Blend(Colors.White, lighten),
            0);
}
