using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Logging;
using Toybox.Studio.Settings;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Extensions;
using Toybox.Studio.PropertyGrid;

namespace Toybox.Studio.Settings;

/// <summary>
/// The Project tab of the Settings panel: edits the open project's settings through the generic property grid,
/// working entirely through the <see cref="SettingsManager.Project"/> asset. The asset owns the schema ↔
/// lean-file reconciliation (when the engine is connected it exposes the full engine-described schema with the
/// project's saved overrides merged in; with no engine it edits the flat file) and the lean save; this view-model
/// just renders its <see cref="Toybox.Studio.Project.Assets.ProjectSettings.Body"/>, tracks dirty state,
/// and commits/reverts through the manager. Buffered like the rest of the panel: edits are held in the asset's
/// working body and committed only by the owning <see cref="SettingsViewModel"/> on Save.
/// </summary>
public sealed partial class ProjectSettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settings;
    private readonly Logger _log;

    // The live document the grid edits in place (the asset's Body) and a clone of it at the last save / rebuild.
    // RecomputeDirty diffs working vs baseline.
    private JObject? _working;
    private JObject? _baseline;

    public ProjectSettingsViewModel(SettingsManager settings, Logger log)
    {
        _settings = settings;
        _log = log;

        // The manager (re)loads the project settings asset on project change / engine connect and raises Changed;
        // rebuild the grid from the (possibly schema-enriched) body each time. Fires immediately to build now.
        _settings.Listen(() => Dispatch.To(DispatchContext.UI, Build));
    }

    /// <summary>Raised whenever <see cref="IsDirty"/> changes, so the owning panel can recompute aggregate dirty.</summary>
    public event Action? DirtyChanged;

    /// <summary>All of the current project's settings, edited through the generic property grid.</summary>
    public ObservableCollection<PropertyViewModel> Properties { get; } = [];

    /// <summary>Whether a project is open; the tab shows an empty-state hint when not.</summary>
    [ObservableProperty]
    public partial bool HasProject { get; private set; }

    /// <summary>Whether the working document differs from the last-saved/built baseline.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Commits the working document (lean) through the settings manager, then re-baselines.</summary>
    public async Task CommitAsync(CancellationToken ct)
    {
        var result = await _settings.SaveProjectAsync(ct).ContinueOnSameContext();
        if (!result.Success)
        {
            _log.Error($"Failed to save project settings: {result.Error}");
            return;
        }

        Rebaseline();
        RecomputeDirty();
    }

    /// <summary>Discards unsaved edits by reloading the project settings asset (which rebuilds the grid fresh).</summary>
    public void Revert() => _settings.ReloadProjectAsync().FireAndForget();

    // Rebuilds the grid from the project settings asset's body (the type-driven grid mutates the backing JObject
    // in place; CommitAsync persists it). Runs on construction and whenever the manager republishes.
    private void Build()
    {
        Properties.Clear();
        _working = null;
        _baseline = null;

        HasProject = _settings.Project is not null;
        if (_settings.Project?.Body is not { } document)
        {
            RecomputeDirty();
            return;
        }

        foreach (var (descriptor, accessor) in JsonDescriptorReader.Read(document, _ => OnEdited))
            Properties.Add(PropertyViewModelFactory.Create(descriptor, accessor));

        // The grid edits `document` in place; snapshot it as the baseline this build's dirty state diffs against.
        _working = document;
        _baseline = (JObject)document.DeepClone();
        RecomputeDirty();
    }

    // A leaf committed an edit (the live document was already mutated in place); re-derive the dirty state.
    private void OnEdited() => RecomputeDirty();

    private void RecomputeDirty()
    {
        var dirty = _working is { } working && _baseline is { } baseline
            && !JToken.DeepEquals(working, baseline);
        if (dirty == IsDirty)
            return;

        IsDirty = dirty;
        DirtyChanged?.Invoke();
    }

    private void Rebaseline()
    {
        if (_working is { } working)
            _baseline = (JObject)working.DeepClone();
    }
}
