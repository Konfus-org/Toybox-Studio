using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Newtonsoft.Json;
using Toybox.Studio.Project;

namespace Toybox.Studio.Theming;

/// <summary>
/// Owns the editor's themes: loads the Theme.json files under ~/.toybox/Themes (writing the
/// built-in defaults on first run), and applies the active theme's fonts, rounding, and colors to
/// the live Avalonia resources. The Dark/Light toggle picks between the chosen dark and light
/// themes; FluentTheme renders the rest from the accent and corner-radius resources we set here.
/// </summary>
public sealed class ThemeManager
{
    private static readonly string ThemesDir =
        Path.Combine(Settings.BaseDirectory, "Themes");

    private readonly Settings _settings;
    private readonly List<Theme> _themes = [];
    private readonly List<string> _loadWarnings = [];

    public ThemeManager(Settings settings)
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
    public Theme Active { get; private set; } = Theme.DefaultDark();

    public ThemeMode Variant => _settings.Editor.Theme.Variant;

    public IReadOnlyList<ThemeMode> Variants => [ThemeMode.Dark, ThemeMode.Light];

    /// <summary>
    /// Theme names available for the given variant (for the picker).
    /// </summary>
    public IReadOnlyList<string> ThemeNamesFor(ThemeMode variant) =>
        _themes.Where(t => t.Variant == variant)
            .Select(t => t.Name)
            .ToList();

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
    /// Applies the theme selected by the saved variant + per-variant theme choice.
    /// </summary>
    public void ApplySavedTheme()
    {
        var settings = _settings.Editor.Theme;
        var wanted = settings.IsLight ? settings.LightTheme : settings.DarkTheme;
        var theme = _themes.FirstOrDefault(
                        t => string.Equals(t.Name, wanted, StringComparison.OrdinalIgnoreCase))
                    ?? _themes.FirstOrDefault(t => t.IsLight == settings.IsLight)
                    ?? (settings.IsLight ? Theme.DefaultLight() : Theme.DefaultDark());
        Apply(theme);
    }

    /// <summary>
    /// Switches the base variant (Dark/Light) and applies the matching theme.
    /// </summary>
    public void SetVariant(ThemeMode variant)
    {
        _settings.Editor.Theme.Variant = variant;
        _settings.Save();
        ApplySavedTheme();
    }

    /// <summary>
    /// Selects which theme is used for the current variant and applies it.
    /// </summary>
    public void SetActiveTheme(string name)
    {
        var theme = _themes.FirstOrDefault(
            t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (theme is null)
            return;

        if (theme.IsLight)
            _settings.Editor.Theme.LightTheme = theme.Name;
        else
            _settings.Editor.Theme.DarkTheme = theme.Name;

        _settings.Editor.Theme.Variant = theme.Variant;
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

        app.RequestedThemeVariant = theme.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;

        var resources = app.Resources;
        SetBrush(resources, "ThemePrimaryBrush", theme.Colors.Primary);
        SetBrush(resources, "ThemeSecondaryBrush", theme.Colors.Secondary);
        SetBrush(resources, "ThemeTertiaryBrush", theme.Colors.Tertiary);
        SetBrush(resources, "ThemeErrorBrush", theme.Colors.Error);
        SetBrush(resources, "ThemeWarningBrush", theme.Colors.Warning);
        SetBrush(resources, "ThemeInfoBrush", theme.Colors.Info);
        SetBrush(resources, "ThemeSuccessBrush", theme.Colors.Success);
        SetBrush(resources, "ThemeBackgroundBrush", theme.Colors.Background);
        SetBrush(resources, "ThemeSurfaceBrush", theme.Colors.Surface);
        SetBrush(resources, "ThemeTextBrush", theme.Colors.Text);

        // FluentTheme derives control accents from these; primary doubles as the accent.
        if (TryParse(theme.Colors.Primary, out var accent))
        {
            resources["SystemAccentColor"] = accent;
            resources["SystemAccentColorLight1"] = Blend(accent, Colors.White, 0.3f);
            resources["SystemAccentColorLight2"] = Blend(accent, Colors.White, 0.5f);
            resources["SystemAccentColorLight3"] = Blend(accent, Colors.White, 0.7f);
            resources["SystemAccentColorDark1"] = Blend(accent, Colors.Black, 0.2f);
            resources["SystemAccentColorDark2"] = Blend(accent, Colors.Black, 0.4f);
            resources["SystemAccentColorDark3"] = Blend(accent, Colors.Black, 0.6f);
        }

        // Derived neutrals so every surface, border, and band is theme-driven rather than a hardcoded
        // overlay. They are translucent tints of Text (which always contrasts the background, so they
        // adapt to both dark and light variants) plus a background-toned scrim and a muted text colour.
        if (TryParse(theme.Colors.Text, out var text) && TryParse(theme.Colors.Background, out var background))
        {
            resources["ThemeBorderBrush"] = new SolidColorBrush(WithAlpha(text, 0x2E));
            resources["ThemeHeaderBrush"] = new SolidColorBrush(WithAlpha(text, 0x18));
            resources["ThemeBandBrush"] = new SolidColorBrush(WithAlpha(text, 0x10));
            resources["ThemeWellBrush"] = new SolidColorBrush(WithAlpha(text, 0x0A));
            resources["ThemeMutedTextBrush"] = new SolidColorBrush(Blend(text, background, 0.45f));
            resources["ThemeScrimBrush"] = new SolidColorBrush(WithAlpha(background, 0xB0));
        }

        var radius = new CornerRadius(theme.CornerRadius);
        resources["ControlCornerRadius"] = radius;
        resources["OverlayCornerRadius"] = radius;

        resources["ThemeFontFamily"] = new FontFamily(theme.Font.Family);
        resources["ThemeMonoFontFamily"] = new FontFamily(theme.Font.Monospace);
        resources["ThemeFontSize"] = theme.Font.Size;

        if (notify)
            ThemeChanged?.Invoke();
    }

    private static void SetBrush(IResourceDictionary resources, string key, string hex)
    {
        if (TryParse(hex, out var color))
            resources[key] = new SolidColorBrush(color);
    }

    private static bool TryParse(string hex, out Color color)
    {
        if (Color.TryParse(hex, out color))
            return true;

        color = Colors.Magenta;
        return false;
    }

    private void EnsureDefaults()
    {
        Directory.CreateDirectory(ThemesDir);
        WriteIfMissing(Theme.DefaultDark());
        WriteIfMissing(Theme.DefaultLight());
    }

    private void WriteIfMissing(Theme theme)
    {
        var path = PathFor(theme.Name);
        if (!File.Exists(path))
            File.WriteAllText(path, JsonConvert.SerializeObject(theme, Formatting.Indented));
    }

    private static string PathFor(string themeName) => Path.Combine(ThemesDir, $"{themeName}.json");

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        a.A,
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
