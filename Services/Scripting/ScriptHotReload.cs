using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Extensions;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Services.Settings;

namespace Toybox.Studio.Services.Scripting;

/// <summary>
/// The script editor's hot-reload toggle (the lightning-bolt control on both the inline strip and the
/// popped-out window). It's backed by the persisted <c>Scripting ▸ HotReloadOnSave</c> editor setting, so the
/// two bolts and the Settings panel all share one value — toggling either bolt persists and the other follows.
/// When enabled, saving a script triggers an incremental project rebuild; the running engine's plugin watcher
/// then hot-swaps the recompiled scripts.
/// </summary>
public sealed partial class ScriptHotReload : ObservableObject, IDisposable
{
    private readonly SettingsManager _settings;
    private readonly ProjectBuilder _builder;
    private readonly Logger _log;
    private readonly IDisposable _settingsSubscription;

    private bool _syncing;        // guard: re-reading the setting must not loop back into a re-save
    private bool _building;       // one build at a time
    private bool _rebuildQueued;  // a save arrived mid-build; rebuild exactly once more when it finishes
    private string? _lastSavedFile; // the most recent file behind a queued rebuild, for the log line

    public ScriptHotReload(SettingsManager settings, ProjectBuilder builder, Logger log)
    {
        _settings = settings;
        _builder = builder;
        _log = log;

        // Seed from the persisted setting now and re-sync whenever settings are saved (e.g. the Settings
        // panel changes it), so the bolts always reflect the one stored value. Held so it's disposed for
        // symmetry with ScriptEditorViewModel.
        _settingsSubscription = _settings.Listen(() =>
        {
            _syncing = true;
            Enabled = _settings.Settings.Scripting.HotReloadOnSave;
            _syncing = false;
        });
    }

    /// <summary>Whether a save recompiles the scripts. Two-way bound by the bolt controls; persisted.</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    partial void OnEnabledChanged(bool value)
    {
        if (_syncing)
            return;

        // Toggled live from a bolt — write it back to the setting and persist so it survives restarts and
        // shows in Settings ▸ Scripting.
        _settings.Settings.Scripting.HotReloadOnSave = value;
        _settings.SaveAsync().FireAndForget();
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
                var built = await _builder.BuildAsync(CancellationToken.None).ContinueOnSameContext();
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

    public void Dispose() => _settingsSubscription.Dispose();
}
