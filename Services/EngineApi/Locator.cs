using Toybox.Studio.Services.Project;
using Toybox.Studio.Services.Settings;
using Toybox.Studio.Utils;
namespace Toybox.Studio.Services.EngineApi;

/// <summary>
/// Resolves where the Toybox Engine source tree lives (needed to compile projects against it):
/// the persisted setting first, then a scan of known development layouts, and finally a
/// user-supplied folder.
/// </summary>
public sealed class Locator
{
    private readonly SettingsManager _settings;

    public Locator(SettingsManager settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Raised whenever the resolved engine source path changes (null = not located).
    /// </summary>
    public event Action<string?>? EngineChanged;

    public string? EngineSourcePath { get; private set; }

    public bool IsLocated => EngineSourcePath is not null;

    /// <summary>
    /// Resolves the engine at startup; returns a human-readable description.
    /// </summary>
    public string ResolveAtStartup()
    {
        var configured = _settings.Settings.Engine.SourcePath;
        if (!string.IsNullOrEmpty(configured) && IsEngineSourceDirectory(configured))
        {
            SetEngine(configured, persist: false);
            return $"Engine: {configured}";
        }

        var discovered = ScanKnownPaths();
        if (discovered is not null)
        {
            SetEngine(discovered, persist: true);
            return $"Found engine at {discovered}";
        }

        SetEngine(null, persist: false);
        return "Engine source not found; locate it from the toolbar.";
    }

    /// <summary>
    /// Applies a user-picked engine source folder. Returns false when it is unusable.
    /// </summary>
    public bool TrySetManually(string path)
    {
        if (!IsEngineSourceDirectory(path))
            return false;

        SetEngine(Path.GetFullPath(path), persist: true);
        return true;
    }

    private void SetEngine(string? path, bool persist)
    {
        EngineSourcePath = path;
        if (persist && path is not null)
        {
            _settings.Settings.Engine.SourcePath = path;
            // Persist off the UI thread: the JSON snapshot is taken synchronously here, only the disk write defers.
            _settings.SaveAsync().FireAndForget();
        }

        EngineChanged?.Invoke(path);
    }

    /// <summary>
    /// Scans development layouts: each ancestor of the editor's directory is checked for an
    /// Engine folder holding the engine source, plus an engine/ folder beside the editor (the
    /// future installed layout).
    /// </summary>
    private static string? ScanKnownPaths()
    {
        var installCandidate = Path.Combine(AppContext.BaseDirectory, "engine");
        if (IsEngineSourceDirectory(installCandidate))
            return installCandidate;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 6; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Engine");
            if (IsEngineSourceDirectory(candidate))
                return candidate;
        }

        return null;
    }

    private static bool IsEngineSourceDirectory(string path)
    {
        return File.Exists(Path.Combine(path, "CMakeLists.txt"))
            && Directory.Exists(Path.Combine(path, "engine"))
            && Directory.Exists(Path.Combine(path, "tools", "cmake"));
    }
}
