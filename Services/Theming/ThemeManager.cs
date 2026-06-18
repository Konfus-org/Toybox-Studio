using Toybox.Studio.Models;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.Theming;

/// <summary>
/// The editor's theme coordinator. Owns the active-selection lifecycle by tying together the
/// <see cref="ThemeRepository"/> (the on-disk catalog) and the <see cref="ThemeApplier"/> (the live Avalonia
/// resources), and persists which theme is active to the editor settings. There is no light/dark variant —
/// the user picks one theme from a flat list; a light/dark pair is just two themes named by convention.
/// </summary>
public sealed class ThemeManager
{
    private readonly EditorSettings _settings;
    private readonly ThemeRepository _repository = new();
    private readonly ThemeApplier _applier = new();

    public ThemeManager(EditorSettings settings)
    {
        _settings = settings;
        ApplySavedTheme();
    }

    /// <summary>Raised after a theme is applied so dependents (e.g. the engine) can re-sync.</summary>
    public event Action? ThemeChanged
    {
        add => _applier.ThemeChanged += value;
        remove => _applier.ThemeChanged -= value;
    }

    public string ThemesDirectory => _repository.ThemesDirectory;

    public IReadOnlyList<Theme> Themes => _repository.Themes;

    /// <summary>All loaded theme names, for the picker.</summary>
    public IReadOnlyList<string> ThemeNames => _repository.ThemeNames;

    /// <summary>
    /// Non-fatal problems from the last reload (e.g. malformed theme files). Theme loading runs before the
    /// logger exists, so the caller flushes these once it does.
    /// </summary>
    public IReadOnlyList<string> LoadWarnings => _repository.LoadWarnings;

    /// <summary>The currently applied theme.</summary>
    public Theme Active => _applier.Active;

    /// <summary>Re-reads every Theme.json from disk, then re-applies the active selection.</summary>
    public void Reload()
    {
        _repository.Reload();
        ApplySavedTheme();
    }

    /// <summary>
    /// Applies the saved active theme, falling back to any loaded theme and finally the clay default.
    /// </summary>
    public void ApplySavedTheme() => _applier.Apply(_repository.ResolveSaved(_settings.Theme.Active));

    /// <summary>Selects the active theme by name and applies it.</summary>
    public void SetActiveTheme(string name)
    {
        var theme = _repository.Find(name);
        if (theme is null)
            return;

        _settings.Theme.Active = theme.Name;
        // Persist off the UI thread: the JSON snapshot is taken synchronously here, only the disk write defers.
        _settings.SaveAsync().FireAndForget();
        _applier.Apply(theme);
    }

    /// <summary>
    /// Persists an edited theme to its Theme.json and re-applies if it is active. Built-in defaults are
    /// read-only and are never overwritten.
    /// </summary>
    public void SaveTheme(Theme theme)
    {
        if (!_repository.SaveTheme(theme))
            return;

        if (string.Equals(theme.Name, Active.Name, StringComparison.OrdinalIgnoreCase))
            _applier.Apply(theme);
    }

    /// <summary>
    /// Authors a brand-new theme (validates and writes it) without selecting or applying it — the caller
    /// decides whether to switch. Returns false with a reason on failure.
    /// </summary>
    public bool TryCreateTheme(Theme theme, out string? error) => _repository.TryCreateTheme(theme, out error);

    /// <summary>
    /// Imports a theme from an arbitrary .json file under a free name, without selecting it. Returns false
    /// with a reason if the file isn't a readable, valid theme.
    /// </summary>
    public bool TryImportTheme(string sourcePath, out string? error, out string? importedName) =>
        _repository.TryImportTheme(sourcePath, out error, out importedName);

    /// <summary>
    /// Applies a theme to the running UI without persisting it as the active selection or notifying
    /// dependents — used by the Theme Creator to show edits live.
    /// </summary>
    public void PreviewTheme(Theme theme) => _applier.Apply(theme, notify: false);

    /// <summary>
    /// Applies a theme by name to the running UI for preview WITHOUT persisting it as the saved selection.
    /// Used by the Settings panel so a picked theme shows live but only commits on Save (and so Cancel can
    /// re-apply the previously-saved theme by name).
    /// </summary>
    public void PreviewTheme(string name)
    {
        var theme = _repository.Find(name);
        if (theme is not null)
            _applier.Apply(theme);
    }

    /// <summary>Applies a theme to the live resources, optionally raising <see cref="ThemeChanged"/>.</summary>
    public void Apply(Theme theme, bool notify = true) => _applier.Apply(theme, notify);
}
