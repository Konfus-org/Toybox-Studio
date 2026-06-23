using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Services.Settings;
using Toybox.Studio.Utils;
using Toybox.Studio.Widgets.PropertyGrid;

namespace Toybox.Studio.Widgets.Settings;

/// <summary>
/// The Project tab of the Settings panel: edits the open project's lean AppSettings.json through the generic
/// property grid. When the engine is connected it renders the full engine-described schema (graphics, physics,
/// the resizable plugins list, …) with the project's saved overrides merged in, and persists a lean document
/// (only fields that differ from the engine defaults). With no engine it falls back to editing the flat file.
///
/// Reconciling the engine's described schema with the lean on-disk file is this view-model's own concern (the
/// former <c>AppSettingsDocument</c> helper): <see cref="Merge"/> overlays the saved values onto a clone of the
/// schema for editing, and <see cref="BuildLean"/> turns the edited schema back into a lean document to persist.
/// Buffered like the rest of the panel: edits are held in a working copy and committed only by the owning
/// <see cref="SettingsViewModel"/> on Save.
/// </summary>
public sealed partial class ProjectSettingsViewModel : ObservableObject
{
    private readonly ProjectManager _projects;
    private readonly EngineSettings _engineSettings;
    private readonly JsonParser _parser;
    private readonly Logger _log;

    // The described AppSettings schema (attributed defaults), fetched once per session — it is
    // project-independent, so it is cached and reused for every project.
    private JObject? _settingsSchema;

    // The open project's app settings (AppSettings.json, asset type tbx::AppSettings) as the lean on-disk
    // JObject, or null when no project is open / the file can't be read. Reloaded by Refresh.
    private JObject? _appJson;

    // When the engine schema is available, the grid edits this merged document (the described defaults with
    // the on-disk values overlaid) in place, and _defaults (a pristine clone of the schema) is the reference
    // the lean save diffs against. Both null on the flat fallback path (engine not connected).
    private JObject? _doc;
    private JObject? _defaults;

    // Dirty tracking: the live document the grid edits (schema path = _doc, flat fallback = _appJson) and a
    // clone of it at the last save / (re)build. RecomputeDirty diffs working vs baseline.
    private JObject? _working;
    private JObject? _baseline;

    public ProjectSettingsViewModel(
        ProjectManager projects,
        EngineSettings engineSettings,
        JsonParser parser,
        Logger log)
    {
        _projects = projects;
        _engineSettings = engineSettings;
        _parser = parser;
        _log = log;

        projects.ProjectChanged += _ => Dispatch.To(DispatchContext.UI, Refresh);
        Refresh();
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

    /// <summary>Fetches the engine schema (once), then rebuilds the project grid enriched, on the UI thread.</summary>
    public async Task LoadSchemaThenRefreshAsync()
    {
        await EnsureSchemaAsync().ContinueOnAnyContext();
        Dispatch.To(DispatchContext.UI, Refresh);
    }

    /// <summary>Commits the working document to disk/engine (lean), then re-baselines. Called on the panel's Save.</summary>
    public async Task CommitAsync(CancellationToken ct)
    {
        await SaveAsync(ct).ContinueOnSameContext();
        Rebaseline();
        RecomputeDirty();
    }

    /// <summary>
    /// Discards unsaved edits by re-reading the on-disk AppSettings.json and rebuilding the grid (which
    /// re-baselines and clears the dirty flag).
    /// </summary>
    public void Revert()
    {
        // The flat-fallback grid edits _appJson in place, so re-read it from disk to undo those edits; the
        // schema path rebuilds _doc fresh from the schema + this re-read file regardless.
        _appJson = ReadAppJson(_projects.CurrentProject);
        Build();
    }

    // Re-reads the current project's file and rebuilds the grid; reused on project change and engine connect.
    private void Refresh()
    {
        var project = _projects.CurrentProject;
        HasProject = project is not null;
        _appJson = ReadAppJson(project);
        Build();
    }

    private async Task EnsureSchemaAsync()
    {
        if (_settingsSchema is not null)
            return;

        var result = await _engineSettings.DescribeSchemaAsync(CancellationToken.None).ContinueOnAnyContext();
        if (result is { Success: true, Value: { } schema })
            _settingsSchema = schema;
    }

    // Builds the grid from the on-disk values, enriched with the engine schema when available (the type-driven
    // grid mutates the backing JObject in place; CommitAsync persists it).
    private void Build()
    {
        Properties.Clear();
        _doc = null;
        _defaults = null;
        _working = null;
        _baseline = null;
        if (_appJson is not { } app)
        {
            RecomputeDirty();
            return;
        }

        // Preferred path: render the full engine-described schema (graphics/physics/etc., and a resizable
        // plugins list) with the project's saved values overlaid. The flat file is the fallback used until
        // the engine connects and the schema arrives.
        JObject document = app;
        if (_settingsSchema is { } schema)
        {
            _defaults = (JObject)schema.DeepClone();
            _doc = (JObject)schema.DeepClone();
            Merge(_doc, app);
            document = _doc;
        }

        foreach (var node in _parser.ParseProperties(document))
            Properties.Add(PropertyViewModelFactory.Create(node, OnEdited));

        // The grid edits `document` in place; snapshot it as the baseline this build's dirty state diffs against.
        _working = document;
        _baseline = (JObject)document.DeepClone();
        RecomputeDirty();
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        if (_projects.CurrentProject is not { } project)
            return;

        // Schema path: persist a lean document (only the fields that differ from the engine defaults), exactly
        // as the engine's own omit-defaults serializer would. Fallback path: write the flat file as-is.
        var toWrite = _doc is { } edited && _defaults is { } defaults
            ? BuildLean(edited, defaults)
            : _appJson;
        if (toWrite is null)
            return;

        // Persist through the engine's generic asset save (it owns the AppSettings serializer) when connected;
        // fall back to writing the file directly so settings stay editable with no running engine.
        if (_engineSettings.IsConnected)
        {
            var result = await _engineSettings
                .SaveAsync("AppSettings", project.AppSettingsPath, toWrite, ct)
                .ContinueOnSameContext();
            if (!result.Success)
            {
                _log.Error($"Failed to save project settings: {result.Error}");
                return;
            }
        }
        else
        {
            try
            {
                await File.WriteAllTextAsync(project.AppSettingsPath, toWrite.ToString(Formatting.Indented), ct)
                    .ContinueOnAnyContext();
            }
            catch (Exception exception)
            {
                _log.Error($"Failed to save project settings: {exception.Message}");
                return;
            }
        }

        _appJson = toWrite;
        // Deliberately NOT _projects.Reopen() here: that fired ProjectChanged → Session.RestartForProjectAsync,
        // relaunching/recompiling the engine on every settings save. The running engine hot-reloads AppSettings
        // on its own (it watches the file), and our _appJson is already current — so just refresh the project
        // display name (which can change via the "name" field) without notifying Session.
        _projects.RefreshDisplayName();
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

    private static JObject? ReadAppJson(ProjectInfo? project)
    {
        if (project is null)
            return null;

        try
        {
            return JObject.Parse(File.ReadAllText(project.AppSettingsPath));
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---- Schema ↔ lean-file reconciliation (formerly AppSettingsDocument) ----

    /// <summary>
    /// Overlays the lean on-disk document's values onto <paramref name="schema"/> (a clone of the described
    /// schema) in place. The schema is the authority for structure and defaults; a value present on disk wins,
    /// and everything the file omits keeps its engine default. Keys on disk that the schema doesn't know are
    /// ignored (stale settings drop out).
    /// </summary>
    private static void Merge(JObject schema, JObject disk)
    {
        foreach (var diskField in disk.Properties())
            if (schema[diskField.Name] is JObject schemaField)
                MergeField(schemaField, Inner(diskField.Value));
    }

    private static void MergeField(JObject schemaField, JToken diskValue)
    {
        var schemaValue = schemaField["value"];
        if (schemaValue is JObject nested && IsComposite(nested) && diskValue is JObject diskObject)
        {
            foreach (var diskChild in diskObject.Properties())
                if (nested[diskChild.Name] is JObject childField)
                    MergeField(childField, Inner(diskChild.Value));
        }
        else
        {
            // Leaf or array — the disk value replaces the default wholesale (the field's attributes,
            // including the array element_template, are left intact).
            schemaField["value"] = diskValue.DeepClone();
        }
    }

    /// <summary>
    /// Builds the lean <c>{ type, value }</c> document to persist from the edited schema, emitting only fields
    /// whose value differs from <paramref name="defaults"/> (a pristine clone of the described schema). A
    /// nested struct recurses and is written only when at least one descendant differs, so an all-default
    /// subsection is omitted entirely — matching the engine's own omit-defaults output.
    /// </summary>
    private static JObject BuildLean(JObject edited, JObject defaults)
    {
        var lean = new JObject();
        foreach (var field in edited.Properties())
            if (defaults[field.Name] is JObject defaultField && field.Value is JObject editedField
                && LeanField(editedField, defaultField) is { } leanField)
                lean[field.Name] = leanField;

        return lean;
    }

    private static JObject? LeanField(JObject editedField, JObject defaultField)
    {
        // The lean shape carries the type token at the top level; the described field keeps it under
        // "attributes". (The bare-value fallback path has neither, leaving type null — harmless, since the
        // engine reads each value by its static field type and treats the token as cosmetic.)
        var type = (editedField["attributes"]?["type"] ?? editedField["type"])?.DeepClone();
        var editedValue = editedField["value"];
        var defaultValue = defaultField["value"];

        if (editedValue is JObject nested && IsComposite(nested) && defaultValue is JObject defaultNested)
        {
            var leanChildren = new JObject();
            foreach (var child in nested.Properties())
                if (defaultNested[child.Name] is JObject defaultChild && child.Value is JObject childField
                    && LeanField(childField, defaultChild) is { } leanChild)
                    leanChildren[child.Name] = leanChild;

            return leanChildren.Count == 0
                ? null
                : new JObject { ["type"] = type, ["value"] = leanChildren };
        }

        return JToken.DeepEquals(editedValue, defaultValue)
            ? null
            : new JObject { ["type"] = type, ["value"] = editedValue?.DeepClone() };
    }

    // Unwraps a typed/attributed field to its inner value token; bare values pass through unchanged.
    private static JToken Inner(JToken token) =>
        token is JObject obj && obj.TryGetValue("value", out var value) ? value : token;

    // A schema field's value is a nested struct (recurse field-by-field) when its members are themselves
    // field wrappers — each an object carrying its own "value". A scalar/array/leaf value is not.
    private static bool IsComposite(JToken value) =>
        value is JObject obj && obj.Properties().Any(p => p.Value is JObject child && child.ContainsKey("value"));
}
