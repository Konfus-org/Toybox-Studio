using System;
using System.Linq;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Project;

/// <summary>
/// Watches the open project's folder for asset file changes and refreshes the <see cref="AssetCatalog"/> so the
/// browser auto-detects assets added, removed or renamed outside the editor (an FBX dropped in, a Blender export,
/// a hand-edited material). The engine has its own asset file watcher; this mirrors it on the editor side so the
/// browser stays in step without a manual refresh. A burst of file events is debounced into a single refresh, and
/// build-output churn is ignored. Re-targets whenever the open project changes.
/// </summary>
public sealed class AssetWatcher : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(350);

    private readonly AssetCatalog _catalog;
    private readonly Logger _log;
    private readonly object _gate = new();

    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    public AssetWatcher(ProjectManager projects, AssetCatalog catalog, Logger log)
    {
        _catalog = catalog;
        _log = log;

        projects.ProjectChanged += OnProjectChanged;
        if (projects.CurrentProject is { } project)
            Watch(project.RootDirectory);
    }

    public void Dispose()
    {
        StopWatching();
        lock (_gate)
        {
            _debounce?.Dispose();
            _debounce = null;
        }
    }

    private void OnProjectChanged(ProjectInfo? project)
    {
        StopWatching();
        if (project is not null)
            Watch(project.RootDirectory);
    }

    private void Watch(string root)
    {
        if (!Directory.Exists(root))
            return;

        try
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnChanged;
            watcher.Changed += OnChanged;
            // A dropped overflow still means "something changed" — schedule a refresh rather than miss it.
            watcher.Error += (_, _) => Schedule();
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception exception)
        {
            _log.Warning($"Asset watcher could not watch '{root}': {exception.Message}");
        }
    }

    private void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // Build output churns constantly and isn't browsable content — ignore it (the browser hides it too).
        if (IsUnderBuild(e.FullPath))
            return;

        Schedule();
    }

    // Coalesces a burst of file events into one refresh: each event (re)arms the timer, so the catalog refresh
    // fires once the changes settle rather than once per file.
    private void Schedule()
    {
        lock (_gate)
        {
            _debounce ??= new Timer(_ => Dispatch.To(DispatchContext.UI, () => _catalog.RefreshAsync().FireAndForget()));
            _debounce.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private static bool IsUnderBuild(string fullPath) =>
        fullPath.Split('/', '\\').Any(segment => segment.Equals("build", StringComparison.OrdinalIgnoreCase));
}
