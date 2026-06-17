using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Newtonsoft.Json;
using Toybox.Studio.Models;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.Theming;

/// <summary>
/// Owns the editor's themes: loads the Theme.json files under ~/.toybox/Themes (writing the
/// built-in defaults on first run), and applies the active theme's fonts, rounding, and colors to
/// the live Avalonia resources. There is no light/dark variant — the user picks one theme from a flat
/// list, and the Avalonia base variant is derived from the theme's Background so FluentTheme renders
/// its controls correctly. A light/dark pair is just two themes named by convention.
/// </summary>
public sealed class ThemeManager
{
    private static readonly string ThemesDir =
        Path.Combine(EditorSettings.BaseDirectory, "Themes");

    // Dock's drop-target selector squares are baked white PNGs (a window glyph with a warm/yellow dock-edge
    // highlight) shipped in Dock.Avalonia.Themes.Fluent. We recolour copies into these resource keys, and
    // AppStyles points each DockTarget/GlobalDockTarget selector Image at the matching key.
    private static readonly (string Key, string Asset)[] DockSelectorAssets =
    [
        ("DockSelectorTopImage", "DockAnchorableTop"),
        ("DockSelectorBottomImage", "DockAnchorableBottom"),
        ("DockSelectorLeftImage", "DockAnchorableLeft"),
        ("DockSelectorRightImage", "DockAnchorableRight"),
        ("DockSelectorInsideImage", "DockDocumentInside"),
    ];

    private readonly EditorSettings _settings;
    private readonly List<Theme> _themes = [];
    private readonly List<string> _loadWarnings = [];

    public ThemeManager(EditorSettings settings)
    {
        _settings = settings;
        EnsureDefaults();
        Reload();
    }

    /// <summary>
    /// Raised after a theme is applied so dependents (e.g. the engine) can re-sync.
    /// </summary>
    public event Action? ThemeChanged;

    public string ThemesDirectory => ThemesDir;

    public IReadOnlyList<Theme> Themes => _themes;

    /// <summary>
    /// Non-fatal problems from the last <see cref="Reload"/> (e.g. malformed theme files). Theme loading
    /// runs before the logger exists, so the caller flushes these once it does.
    /// </summary>
    public IReadOnlyList<string> LoadWarnings => _loadWarnings;

    /// <summary>
    /// The currently applied theme.
    /// </summary>
    public Theme Active { get; private set; } = Theme.DefaultClay();

    /// <summary>
    /// All loaded theme names, for the picker.
    /// </summary>
    public IReadOnlyList<string> ThemeNames =>
        _themes.Select(t => t.Name).ToList();

    /// <summary>
    /// Re-reads every Theme.json from disk, then re-applies the active selection.
    /// </summary>
    public void Reload()
    {
        _themes.Clear();
        _loadWarnings.Clear();
        foreach (var file in Directory.EnumerateFiles(ThemesDir, "*.json"))
        {
            try
            {
                var theme = JsonConvert.DeserializeObject<Theme>(File.ReadAllText(file));
                if (theme is not null && !string.IsNullOrWhiteSpace(theme.Name))
                    _themes.Add(theme);
            }
            catch (Exception exception)
            {
                // A malformed theme file is skipped rather than blocking startup, but recorded so the
                // user finds out why their theme is missing.
                _loadWarnings.Add($"Skipped malformed theme '{Path.GetFileName(file)}': {exception.Message}");
            }
        }

        ApplySavedTheme();
    }

    /// <summary>
    /// Applies the saved active theme, falling back to any loaded theme and finally the clay default.
    /// </summary>
    public void ApplySavedTheme()
    {
        var wanted = _settings.Theme.Active;
        var theme = _themes.FirstOrDefault(
                        t => string.Equals(t.Name, wanted, StringComparison.OrdinalIgnoreCase))
                    ?? _themes.FirstOrDefault()
                    ?? Theme.DefaultClay();
        Apply(theme);
    }

    /// <summary>
    /// Selects the active theme by name and applies it.
    /// </summary>
    public void SetActiveTheme(string name)
    {
        var theme = _themes.FirstOrDefault(
            t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (theme is null)
            return;

        _settings.Theme.Active = theme.Name;
        _settings.Save();
        Apply(theme);
    }

    /// <summary>
    /// Persists an edited theme to its Theme.json and re-applies if it is active. Built-in defaults are
    /// read-only and are never overwritten.
    /// </summary>
    public void SaveTheme(Theme theme)
    {
        if (theme.IsBuiltIn)
            return;

        WriteAndTrack(theme);

        if (string.Equals(theme.Name, Active.Name, StringComparison.OrdinalIgnoreCase))
            Apply(theme);
    }

    /// <summary>
    /// Authors a brand-new theme: validates the name (not blank, not a reserved built-in name, not a
    /// duplicate) and writes it to disk + the in-memory list. Does NOT select or apply it — the caller
    /// decides whether to switch to it (e.g. after prompting the user). Returns false with a reason on
    /// failure.
    /// </summary>
    public bool TryCreateTheme(Theme theme, out string? error)
    {
        error = null;
        var name = theme.Name?.Trim() ?? "";
        if (name.Length == 0)
        {
            error = "Enter a theme name.";
            return false;
        }

        if (string.Equals(name, Theme.DarkName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, Theme.LightName, StringComparison.OrdinalIgnoreCase))
        {
            error = $"'{name}' is a reserved built-in theme name.";
            return false;
        }

        if (_themes.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"A theme named '{name}' already exists.";
            return false;
        }

        theme.Name = name;
        WriteAndTrack(theme);
        return true;
    }

    /// <summary>
    /// Imports a theme from an arbitrary .json file on disk: reads + validates it, lands it under a free name
    /// (so importing never clobbers a built-in or an existing user theme), and copies it into the themes
    /// folder + the in-memory list. Does NOT select it — the caller decides whether to switch. Returns false
    /// with a reason if the file isn't a readable, valid theme.
    /// </summary>
    public bool TryImportTheme(string sourcePath, out string? error, out string? importedName)
    {
        error = null;
        importedName = null;

        Theme? theme;
        try
        {
            theme = JsonConvert.DeserializeObject<Theme>(File.ReadAllText(sourcePath));
        }
        catch (Exception exception)
        {
            error = $"Couldn't read that theme file: {exception.Message}";
            return false;
        }

        if (theme is null || string.IsNullOrWhiteSpace(theme.Name))
        {
            error = "That file isn't a valid theme.";
            return false;
        }

        theme.Name = UniqueThemeName(theme.Name.Trim());
        WriteAndTrack(theme);
        importedName = theme.Name;
        return true;
    }

    /// <summary>
    /// Returns <paramref name="desired"/> if it's free (not a reserved built-in name, not already loaded),
    /// otherwise the same name with a " (2)", " (3)", … suffix until one is.
    /// </summary>
    private string UniqueThemeName(string desired)
    {
        bool Taken(string name) =>
            string.Equals(name, Theme.DarkName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, Theme.LightName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, Theme.ClayName, StringComparison.OrdinalIgnoreCase)
            || _themes.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

        if (!Taken(desired))
            return desired;

        for (var n = 2; ; n++)
        {
            var candidate = $"{desired} ({n})";
            if (!Taken(candidate))
                return candidate;
        }
    }

    private void WriteAndTrack(Theme theme)
    {
        Directory.CreateDirectory(ThemesDir);
        File.WriteAllText(PathFor(theme.Name), JsonConvert.SerializeObject(theme, Formatting.Indented));

        var index = _themes.FindIndex(
            t => string.Equals(t.Name, theme.Name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            _themes[index] = theme;
        else
            _themes.Add(theme);
    }

    /// <summary>
    /// Applies a theme to the running UI without persisting it as the active selection or notifying
    /// dependents — used by the Theme Creator to show edits live. Reverting is just another preview (or a
    /// real <see cref="Apply"/>) back to the previous theme.
    /// </summary>
    public void PreviewTheme(Theme theme) => Apply(theme, notify: false);

    /// <summary>
    /// Writes every theme token onto the live Avalonia resource dictionary. When <paramref name="notify"/>
    /// is true (the default) it also raises <see cref="ThemeChanged"/> so dependents (e.g. the engine log
    /// colors) re-sync; live preview passes false to avoid spamming that work on every edit.
    /// </summary>
    public void Apply(Theme theme, bool notify = true)
    {
        Active = theme;
        if (Application.Current is not { } app)
            return;

        var colors = theme.Colors;
        app.RequestedThemeVariant = IsLight(colors.Background) ? ThemeVariant.Light : ThemeVariant.Dark;

        var resources = app.Resources;

        // AUTO-CONTRAST TEXT: the theme provides a BASE text colour; we then push it lighter or darker
        // (whichever moves away from the background) until it clears a WCAG-style contrast ratio, so text is
        // always legible no matter what colours the theme picks. Primary text aims high (~6.5:1); muted text
        // is allowed to sit lower (~3.2:1) so it still reads as secondary without becoming illegible.
        var background = colors.Background.Representative;
        var text = EnsureContrast(colors.Text.Start, background, 8.5);
        // On-colour ink: text that aggressively contrasts the PRIMARY colour (used wherever text/icons sit on
        // a primary-coloured fill — action buttons, the focused panel header, active chrome glyphs).
        var onPrimary = EnsureContrast(colors.Primary.Representative, colors.Primary.Representative, 8.5);

        SetBrush(resources, "ThemePrimaryBrush", colors.Primary);
        SetBrush(resources, "ThemeSecondaryBrush", colors.Secondary);
        SetBrush(resources, "ThemeTertiaryBrush", colors.Tertiary);
        SetBrush(resources, "ThemeErrorBrush", colors.Error);
        SetBrush(resources, "ThemeWarningBrush", colors.Warning);
        SetBrush(resources, "ThemeInfoBrush", colors.Info);
        SetBrush(resources, "ThemeSuccessBrush", colors.Success);
        SetBrush(resources, "ThemeBackgroundBrush", colors.Background);
        SetBrush(resources, "ThemeSurfaceBrush", colors.Surface);
        resources["ThemeTextBrush"] = new SolidColorBrush(text);

        // FluentTheme derives control accents from a single colour; the primary gradient's start stop is
        // its dominant colour, so it stands in as the accent.
        var accent = colors.Primary.Start;
        resources["SystemAccentColor"] = accent;
        resources["SystemAccentColorLight1"] = Blend(accent, Colors.White, 0.3f);
        resources["SystemAccentColorLight2"] = Blend(accent, Colors.White, 0.5f);
        resources["SystemAccentColorLight3"] = Blend(accent, Colors.White, 0.7f);
        resources["SystemAccentColorDark1"] = Blend(accent, Colors.Black, 0.2f);
        resources["SystemAccentColorDark2"] = Blend(accent, Colors.Black, 0.4f);
        resources["SystemAccentColorDark3"] = Blend(accent, Colors.Black, 0.6f);

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

        // Derived neutrals — translucent tints of the (auto-contrasted) text colour for borders/bands/wells,
        // a background-toned scrim, and a muted text colour that still clears a lower contrast floor.
        resources["ThemeBorderBrush"] = new SolidColorBrush(WithAlpha(text, 0x2E));
        resources["ThemeHeaderBrush"] = new SolidColorBrush(WithAlpha(text, 0x18));
        resources["ThemeBandBrush"] = new SolidColorBrush(WithAlpha(text, 0x10));
        resources["ThemeWellBrush"] = new SolidColorBrush(WithAlpha(text, 0x0A));
        resources["ThemeMutedTextBrush"] =
            new SolidColorBrush(EnsureContrast(Blend(text, background, 0.40f), background, 4.5));
        resources["ThemeScrimBrush"] = new SolidColorBrush(WithAlpha(background, 0xB0));

        // Dock.Avalonia ships its own #007ACC accent + system-derived chrome; point its overridable brush
        // keys at the active theme so docking (selected tabs, the drop-position highlight, the drag overlay
        // backdrop, and panel chrome) tracks the theme's colours. Surface/background follow the gradients;
        // the accent uses the primary's dominant stop. The drop-target selector squares are baked white PNGs
        // in Dock — we multiply-tint them to the primary at runtime (see TintDockSelectors) and AppStyles
        // points the selector Images at the recoloured bitmaps.
        var dockBg = background;
        var primaryStart = colors.Primary.Start;
        var primaryEnd = colors.Primary.End;

        // VERIFIED against Dock's Fluent/Accents/Fluent.axaml + ToolChromeControl.axaml (the real keys):
        //  • the ACTIVE panel header (Grid#PART_Grip in the :active state) fills with DockSurfaceHeaderActiveBrush
        //  • inactive header fills with DockSurfacePanelBrush
        //  • the panel OUTLINE (Border#PART_Border) uses DockBorderSubtleBrush / DockBorderStrongBrush
        // Earlier passes set the wrong keys, which is why the title stayed flat and the outline stayed.
        var primaryBrushForDock = colors.Primary.ToBrush();
        // Flat (non-gradient) primary — for small elements where a gradient reads badly (the dock tab, the
        // focused-panel outline).
        var accentSolid = new SolidColorBrush(colors.Primary.Representative);
        resources["ThemeAccentSolidBrush"] = accentSolid;
        // Panel surface — a gentle warm vertical ramp. The top stop is warmed (NOT the near-white surface
        // start) so the panel's top edge doesn't read as a bright white border against the window.
        var dockSurface = ColorGradient.BuildBrush(
            Blend(colors.Surface.Start, colors.Surface.End, 0.5f), colors.Surface.End, 90);

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
        resources["DockTargetIndicatorBrush"] = new SolidColorBrush(WithAlpha(accent, 0x80));

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

        resources["DockSelectorOverlayBackdropBrush"] = new SolidColorBrush(WithAlpha(dockBg, 0xC0));

        // Chrome buttons (pin / menu / close): themed icon + a faint hover band, no fill of their own.
        resources["DockToolChromeIconBrush"] = new SolidColorBrush(text);
        resources["DockChromeButtonForegroundBrush"] = new SolidColorBrush(text);
        // No drag-grip bar in the title bars — the whole header is draggable anyway.
        resources["DockChromeGripBrush"] = new SolidColorBrush(Colors.Transparent);
        resources["DockChromeButtonHoverBackgroundBrush"] = new SolidColorBrush(WithAlpha(text, 0x18));
        resources["DockChromeButtonPressedBackgroundBrush"] = new SolidColorBrush(WithAlpha(text, 0x28));

        // Tabs (tool + document): clay feel — transparent idle, faint hover, the surface gradient behind the
        // ACTIVE tab with an accent indicator + accent text.
        resources["DockTabBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
        resources["DockDocumentTabStripBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
        resources["DockTabHoverBackgroundBrush"] = new SolidColorBrush(WithAlpha(text, 0x14));
        // Active tab has a PURPLE (primary) background, so its text/indicator use the on-primary ink. Inactive
        // tab text is the auto-contrast muted ink (on the cream strip).
        resources["DockTabActiveBackgroundBrush"] = primaryBrushForDock;
        resources["DockTabActiveIndicatorBrush"] = primaryBrushForDock;
        // The SELECTED tab is forced purple (primary) for both :selected states by an AppStyles rule that sets
        // the tab ITEM's Background (PART_TabBody binds TemplateBinding Background) → so all selected-tab text
        // is on-primary white; unselected/hover-unselected text is muted/dark on the cream strip.
        var onPrimaryBrush = new SolidColorBrush(onPrimary);
        var darkInk = new SolidColorBrush(text);
        resources["DockTabForegroundBrush"] = new SolidColorBrush(EnsureContrast(Blend(text, background, 0.35f), background, 4.5));
        resources["DockTabSelectedForegroundBrush"] = onPrimaryBrush;
        resources["DockTabActiveForegroundBrush"] = onPrimaryBrush;
        resources["DockDocumentTabSelectedForegroundBrush"] = onPrimaryBrush;
        resources["DockDocumentTabPointerOverForegroundBrush"] = darkInk;

        // Targeted recolour of Dock's white drop-target selector PNGs: the warm/yellow dock-edge highlight
        // becomes the primary accent; the neutral window glyph is tinted between Text and Surface by its
        // lightness, so it reads dark-on-light or light-on-dark automatically.
        TintDockSelectors(resources, accent, text, colors.Surface.Representative);

        // FLAT control surfaces (gradients removed): text inputs are near-white pills (with the inset well
        // shadow they read as the clean white fields in the reference), buttons are a flat surface tone.
        var flatBorder = new SolidColorBrush(WithAlpha(text, 0x22));
        var transparent = new SolidColorBrush(Colors.Transparent);
        var inputFill = new SolidColorBrush(colors.Surface.Start);
        var inputHover = new SolidColorBrush(Blend(colors.Surface.Start, Colors.White, 0.4f));
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

        // Consistent disabled chrome across ALL buttons (the Game transport, the World add/refresh, dialog
        // buttons …) — a muted surface fill + muted text derived from the theme, so disabled never looks
        // different from one widget to the next.
        var disabledFill = new SolidColorBrush(Blend(colors.Surface.Representative, background, 0.5f));
        var disabledInk = new SolidColorBrush(WithAlpha(text, 0x59));
        foreach (var key in new[] { "ButtonBackgroundDisabled", "ComboBoxBackgroundDisabled", "TextControlBackgroundDisabled" })
            resources[key] = disabledFill;
        foreach (var key in new[] { "ButtonForegroundDisabled", "ComboBoxForegroundDisabled", "TextControlForegroundDisabled" })
            resources[key] = disabledInk;
        resources["ThemeDisabledBrush"] = disabledFill;
        resources["ThemeDisabledTextBrush"] = disabledInk;

        // Semantic button colours, all theme-driven and using the SAME top→bottom gradient shading as the
        // standard button: action = primary (brand), play = success (green), stop = error (red). Each has a
        // brightened hover variant (both stops toward white) so hover lifts the colour instead of changing it.
        SetColorButton(resources, "ThemeActionButton", colors.Primary.Start, colors.Primary.End);
        SetColorButton(resources, "ThemePlayButton", colors.Success.Representative, colors.Success.Representative);
        SetColorButton(resources, "ThemeStopButton", colors.Error.Representative, colors.Error.Representative);
        resources["ThemeOnColorTextBrush"] = new SolidColorBrush(onPrimary);

        // Combo boxes: flat near-white pills like the text inputs (no gradient).
        resources["ComboBoxBackground"] = inputFill;
        resources["ComboBoxBackgroundPointerOver"] = inputHover;
        resources["ComboBoxBackgroundPressed"] = new SolidColorBrush(Blend(colors.Surface.Start, Colors.Black, 0.06f));
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

        // Clay depth tokens for the property grid & inputs. A PRESSED well (text boxes, combo boxes, field
        // hosts, the "at default" indicator) casts a dark hairline on its light-facing inner edge with a soft
        // highlight opposite, so it reads as moulded INTO the surface. A RAISED card (each property group, the
        // selected tab) is the mirror — lifted OUT of the surface. Both follow the theme's light angle and
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

        // The pressed-groove hairline between rows (the row template adds a 1px light highlight beneath it).
        // Derived from Text so it adapts to light and dark themes.
        resources["ThemeGrooveBrush"] = new SolidColorBrush(WithAlpha(text, 0x14));

        // Flat fill for field hosts / pickers / the at-default indicator well — matches the flat text inputs
        // set above (no gradient); the inputs and combos themselves are already flat.
        resources["ThemeWellFillBrush"] = inputFill;

        // Cohesive flat surfaces (no gradient): the tabbed content panel uses the theme's surface TONE (its
        // representative colour — warm, not plain white), so the panel reads as themed rather than blank. The
        // section-header band is a flat tint over it for component / category / section headers.
        resources["ThemeSurfaceFlatBrush"] = new SolidColorBrush(colors.Surface.Representative);
        resources["ThemeHeaderRaisedBrush"] = new SolidColorBrush(WithAlpha(text, 0x12));

        // A brightened flat accent for hover affordances (e.g. the reset indicator brightening on hover) —
        // never used as a gradient, and never on text.
        resources["ThemeAccentBrightBrush"] = new SolidColorBrush(Blend(colors.Primary.Representative, Colors.White, 0.28f));

        // A translucent primary tint for menu / toolbar item highlights (hover, open) — clearly the primary
        // colour rather than a neutral grey, while staying light enough to keep the item's dark text legible.
        resources["ThemeMenuHighlightBrush"] = new SolidColorBrush(WithAlpha(colors.Primary.Representative, 0x3A));

        // Tab + panel rounding derived from the theme's CornerRadius: a tab rounds only its TOP corners while
        // the panel rounds only its BOTTOM corners, so the selected tab reads as part of the panel it owns.
        resources["ThemeTabCornerRadius"] = new CornerRadius(theme.CornerRadius, theme.CornerRadius, 0, 0);
        // The tabbed content panel is SQUARE: a rounded bottom would expose the (warmer) surface behind its
        // corners, reading as a shadow over the rounding. The tabs keep their rounded tops; the panel they own
        // is a clean square the selected tab fades into.
        resources["ThemePanelCornerRadius"] = new CornerRadius(0);

        if (notify)
            ThemeChanged?.Invoke();
    }

    /// <summary>A vertical (top→bottom) clay gradient for buttons, lightened at the top and darkened at the
    /// bottom relative to the surface stops, so the button reads as a raised, moulded pill.</summary>
    private static IBrush ButtonGradient(ColorGradient surface, float lightenTop, float darkenBottom) =>
        ColorGradient.BuildBrush(
            Blend(surface.Start, Colors.White, lightenTop),
            Blend(surface.End, Colors.Black, darkenBottom),
            90);

    /// <summary>
    /// Writes a coloured semantic button as a top→bottom gradient (light top, slightly darker bottom) plus a
    /// brightened hover variant under <paramref name="key"/> + "Brush" / + "HoverBrush". Hover blends both
    /// stops toward white so it lifts the colour rather than swapping it.
    /// </summary>
    private static void SetColorButton(IResourceDictionary resources, string key, Color top, Color bottom)
    {
        // A top→bottom gradient in the button's own hue: the top stop lightened and the bottom stop darkened,
        // so it reads as a raised, moulded pill (the same shading as the standard button) rather than a flat
        // block. A single-colour button (top == bottom) still gets the lighten/darken split. Hover brightens
        // both stops toward white so it lifts the colour while keeping the gradient.
        var topStop = Blend(top, Colors.White, 0.16f);
        var bottomStop = Blend(bottom, Colors.Black, 0.14f);
        resources[key + "Brush"] = ColorGradient.BuildBrush(topStop, bottomStop, 90);
        resources[key + "HoverBrush"] = ColorGradient.BuildBrush(
            Blend(topStop, Colors.White, 0.15f), Blend(bottomStop, Colors.White, 0.15f), 90);
    }

    private static void SetBrush(IResourceDictionary resources, string key, ColorGradient gradient) =>
        resources[key] = gradient.ToBrush();

    private static void TintDockSelectors(IResourceDictionary resources, Color accent, Color ink, Color paper)
    {
        var accentHsl = accent.ToHsl();
        foreach (var (key, asset) in DockSelectorAssets)
        {
            var tinted = RecolorAsset($"avares://Dock.Avalonia.Themes.Fluent/Assets/{asset}.png", accentHsl, ink, paper);
            if (tinted is not null)
                resources[key] = tinted;
        }
    }

    /// <summary>
    /// Targeted recolour of a Dock selector PNG: warm/yellow pixels (the dock-edge highlight) take the accent
    /// hue/saturation at their own lightness, and the neutral window glyph is blended between <paramref name="ink"/>
    /// (Text) and <paramref name="paper"/> (Surface) by its lightness — so it reads on any background. Returns
    /// null (leaving Dock's original asset) if anything goes wrong.
    /// </summary>
    private static WriteableBitmap? RecolorAsset(string uri, HslColor accentHsl, Color ink, Color paper)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri(uri));
            using var src = new Bitmap(stream);
            var size = src.PixelSize;
            var stride = size.Width * 4;
            var bytes = stride * size.Height;

            var buffer = new byte[bytes];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                src.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), bytes, stride);
            }
            finally
            {
                handle.Free();
            }

            // Bgra8888, alpha left untouched.
            for (var i = 0; i < bytes; i += 4)
            {
                var hsl = Color.FromRgb(buffer[i + 2], buffer[i + 1], buffer[i]).ToHsl();
                var outc = hsl is { S: > 0.20, H: >= 20 and <= 70 }
                    ? new HslColor(1.0, accentHsl.H, accentHsl.S, hsl.L).ToRgb() // warm dock-edge highlight → accent
                    : Blend(ink, paper, (float)hsl.L);                          // neutral glyph → Text↔Surface
                buffer[i] = outc.B;
                buffer[i + 1] = outc.G;
                buffer[i + 2] = outc.R;
            }

            var bitmap = new WriteableBitmap(size, src.Dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (var fb = bitmap.Lock())
            {
                for (var y = 0; y < size.Height; y++)
                    Marshal.Copy(buffer, y * stride, fb.Address + y * fb.RowBytes, stride);
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
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

    private void EnsureDefaults()
    {
        Directory.CreateDirectory(ThemesDir);
        // Built-ins are rewritten on every launch (not write-if-missing) so palette refreshes ship to
        // existing installs. They're read-only in the editor, so there are no user edits to clobber.
        foreach (var theme in Theme.BuiltIns)
            File.WriteAllText(PathFor(theme.Name), JsonConvert.SerializeObject(theme, Formatting.Indented));
    }

    private static string PathFor(string themeName) => Path.Combine(ThemesDir, $"{themeName}.json");

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>
    /// Adjusts <paramref name="baseColor"/> away from <paramref name="background"/> (darkening it over a light
    /// background, lightening it over a dark one) in small steps until their WCAG contrast ratio reaches
    /// <paramref name="minRatio"/>, or it bottoms/tops out. Lets a theme pick any base text colour and still
    /// guarantee legibility.
    /// </summary>
    private static Color EnsureContrast(Color baseColor, Color background, double minRatio) =>
        Contrast.Ensure(baseColor, background, minRatio);

    /// <summary>
    /// A copy of a surface gradient with both stops shaded toward <paramref name="toward"/> (white to
    /// lighten for hover, black to darken for press) — used to give controls hover/press states that keep
    /// the clay gradient instead of flipping to a flat FluentTheme colour.
    /// </summary>
    private static IBrush ShadeSurface(ColorGradient surface, Color toward, float amount) =>
        ColorGradient.BuildBrush(
            Blend(surface.Start, toward, amount),
            Blend(surface.End, toward, amount),
            surface.Angle);

    /// <summary>
    /// A left→right (angle 0) linear gradient between two primary stops, optionally lightened toward white —
    /// used for the dock title bars and their hover/active variants.
    /// </summary>
    private static IBrush TitleGradient(Color start, Color end, float lighten) =>
        ColorGradient.BuildBrush(
            Blend(start, Colors.White, lighten),
            Blend(end, Colors.White, lighten),
            0);

    private static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        a.A,
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
