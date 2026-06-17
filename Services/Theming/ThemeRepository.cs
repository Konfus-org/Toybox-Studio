using Newtonsoft.Json;
using Toybox.Studio.Models;

namespace Toybox.Studio.Services.Theming;

/// <summary>
/// Owns the on-disk theme catalog: loads the Theme.json files under ~/.toybox/Themes (writing the built-in
/// defaults on every launch), and authors/imports/saves user themes with name validation. It knows nothing
/// about Avalonia resources — applying a theme to the live UI is <see cref="ThemeApplier"/>'s job, coordinated
/// by <see cref="ThemeManager"/>.
/// </summary>
public sealed class ThemeRepository
{
    private static readonly string ThemesDir =
        Path.Combine(EditorSettings.BaseDirectory, "Themes");

    private readonly List<Theme> _themes = [];
    private readonly List<string> _loadWarnings = [];

    public ThemeRepository()
    {
        EnsureDefaults();
        Reload();
    }

    public string ThemesDirectory => ThemesDir;

    public IReadOnlyList<Theme> Themes => _themes;

    public IReadOnlyList<string> ThemeNames => _themes.Select(t => t.Name).ToList();

    /// <summary>
    /// Non-fatal problems from the last <see cref="Reload"/> (e.g. malformed theme files). Theme loading
    /// runs before the logger exists, so the caller flushes these once it does.
    /// </summary>
    public IReadOnlyList<string> LoadWarnings => _loadWarnings;

    /// <summary>Re-reads every Theme.json from disk into the in-memory catalog. Does not apply anything.</summary>
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
    }

    /// <summary>
    /// The theme to apply for the saved selection: the named theme if present, else the first loaded, else the
    /// clay default.
    /// </summary>
    public Theme ResolveSaved(string? wantedName) =>
        Find(wantedName) ?? _themes.FirstOrDefault() ?? Theme.DefaultClay();

    /// <summary>Finds a loaded theme by (case-insensitive) name, or null.</summary>
    public Theme? Find(string? name) =>
        name is null
            ? null
            : _themes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Persists an edited theme to its Theme.json. Built-in defaults are read-only and are never overwritten.
    /// Returns false (no write) for a built-in. Does not apply — the caller re-applies if it's active.
    /// </summary>
    public bool SaveTheme(Theme theme)
    {
        if (theme.IsBuiltIn)
            return false;

        WriteAndTrack(theme);
        return true;
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

    private void EnsureDefaults()
    {
        Directory.CreateDirectory(ThemesDir);
        // Built-ins are rewritten on every launch (not write-if-missing) so palette refreshes ship to
        // existing installs. They're read-only in the editor, so there are no user edits to clobber.
        foreach (var theme in Theme.BuiltIns)
            File.WriteAllText(PathFor(theme.Name), JsonConvert.SerializeObject(theme, Formatting.Indented));
    }

    private static string PathFor(string themeName) => Path.Combine(ThemesDir, $"{themeName}.json");
}
