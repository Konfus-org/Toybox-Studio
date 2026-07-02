using Toybox.Studio.Events;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Shell;

/// <summary>
/// Resolves where the Toybox Engine source tree lives (needed to compile projects against it):
/// the persisted setting first, then a scan of known development layouts.
/// </summary>
public sealed class EngineLocator
{
    private readonly StudioSettings _settings;
    private readonly EventDispatcher _events;

    public EngineLocator(StudioSettings settings, EventDispatcher events)
    {
        _settings = settings;
        _events = events;
    }

    public string? EngineSourcePath { get; private set; }

    /// <summary>
    /// Resolves the engine at startup; returns a human-readable description.
    /// </summary>
    public string ResolveAtStartup()
    {
        var configured = _settings.SourcePath;
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
        return "Engine source not found; place the studio next to your Toybox checkout.";
    }

    private void SetEngine(string? path, bool persist)
    {
        EngineSourcePath = path;
        if (persist && path is not null)
        {
            _settings.SourcePath = path;
            _settings.SaveAsync().FireAndForget();
        }

        _events.Dispatch(new EngineLocated(path));
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

        // Deep enough to climb out of build/bin/<project>/<config>/<tfm> and the repo folder itself,
        // reaching the Toybox root that holds Engine/ beside the editor checkout.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
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
