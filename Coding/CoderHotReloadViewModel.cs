using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Projects;
using Toybox.Studio.Settings;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Coding;

/// <summary>
/// The code editor's hot-reload toggle (the lightning-bolt control pinned in the editor's top-right). It's
/// backed by the persisted <c>Scripting ▸ HotReloadOnSave</c> editor setting, so the bolt and the Settings
/// panel share one value — toggling the bolt persists it, and a change from Settings follows it back (via the
/// dispatched <see cref="EditorSettingsChanged"/>). When enabled, saving a script triggers an incremental
/// project rebuild; the running engine's plugin watcher then hot-swaps the recompiled scripts.
/// </summary>
public sealed partial class CoderHotReloadViewModel : ObservableEventSubscriber, IEventHandler<EditorSettingsChanged>
{
    private readonly SettingsManager _settings;
    private readonly ProjectBuilder _builder;
    private readonly Project _project;
    private readonly Logger _log;

    private bool _syncing;        // guard: re-reading the setting must not loop back into a re-save
    private bool _building;       // one build at a time
    private bool _rebuildQueued;  // a save arrived mid-build; rebuild exactly once more when it finishes
    private string? _lastSavedFile; // the most recent file behind a queued rebuild, for the log line

    public CoderHotReloadViewModel(
        SettingsManager settings, ProjectBuilder builder, Project project, EventDispatcher events, Logger log)
        : base(events)
    {
        _settings = settings;
        _builder = builder;
        _project = project;
        _log = log;

        // Seed from the persisted setting; the EditorSettingsChanged handler keeps it in sync when the value
        // changes elsewhere (e.g. the Settings panel).
        Enabled = settings.Editor.Scripting.HotReloadOnSave;
    }

    /// <summary>Whether a save recompiles the scripts. Two-way bound by the bolt control; persisted.</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    /// <summary>The setting was applied (possibly from the Settings panel); re-sync the bolt to it.</summary>
    public void Handle(in EditorSettingsChanged evt)
    {
        _syncing = true;
        Enabled = evt.Editor.Scripting.HotReloadOnSave;
        _syncing = false;
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_syncing)
            return;

        // Toggled live from the bolt — write it back to the setting and persist so it survives restarts and
        // shows in Settings ▸ Scripting.
        _settings.Editor.Scripting.HotReloadOnSave = value;
        _settings.ApplyAsync().FireAndForget();
    }

    /// <summary>Called after a script is saved; recompiles the project when enabled so the engine reloads it.</summary>
    public void NotifySaved(string path)
    {
        if (!Enabled)
            return;

        // A save mid-build must not be dropped: queue exactly one trailing rebuild so the latest sources get
        // compiled once the in-flight build finishes, instead of leaving the engine on a stale binary.
        if (_building)
        {
            _rebuildQueued = true;
            _lastSavedFile = path;
            _log.Info($"Hot reload: {Path.GetFileName(path)} saved during a build; queued a rebuild.");
            return;
        }

        RebuildAsync(path).FireAndForget();
    }

    private async Task RebuildAsync(string path)
    {
        _building = true;
        try
        {
            // Coalesce any saves that land while this build runs into one trailing rebuild, looping until no
            // further save arrived during the most recent compile.
            do
            {
                _rebuildQueued = false;
                _log.Info($"Hot reload: recompiling scripts after saving {Path.GetFileName(path)}…");
                var built = await _builder
                    .BuildAsync(_project, StudioBuild.Mode, CancellationToken.None)
                    .ContinueOnSameContext();
                _log.Info(built
                    ? "Hot reload: scripts recompiled; the engine will reload them."
                    : "Hot reload: build failed — see the log above.");

                path = _lastSavedFile ?? path;
            }
            while (_rebuildQueued);
        }
        finally
        {
            _building = false;
        }
    }
}
