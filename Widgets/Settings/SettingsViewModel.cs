using Toybox.Studio.Services;
using Toybox.Studio.Utils;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Widgets.PropertyGrid;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Services.Theming;
using Toybox.Studio.Widgets.Theming;
using Toybox.Studio.Models;
using Toybox.Studio.Shell.Panels;

namespace Toybox.Studio.Widgets.Settings;

/// <summary>
/// The Settings panel: edits a buffered working copy of the editor + project settings and commits only on
/// Save (the docked footer), rather than writing to disk on every keystroke. Dirty state (working ≠ last-saved
/// baseline) shows as a '*' on the tab; Cancel reverts to the baseline. The theme is the one preview-live
/// value — picking a theme applies it immediately, but it only persists on Save and reverts on Cancel.
/// </summary>
public sealed partial class SettingsViewModel : DataPanel
{
    public override string BaseTitle => "Settings";

    private readonly EditorSettings _editor;
    private readonly ThemeManager _theme;
    private readonly ProjectManager _projects;
    private readonly Logger _log;
    private readonly JsonParser _parser;
    private readonly EngineRpc _engine;
    private readonly ThemePropertyViewModel _themes;

    public SettingsViewModel(
        EditorSettings editor,
        ThemeManager theme,
        ThemeCreator themeCreator,
        FilePicker filePicker,
        ProjectManager projects,
        Session session,
        EngineRpc engine,
        Logger log,
        JsonParser parser)
    {
        _editor = editor;
        _theme = theme;
        _projects = projects;
        _log = log;
        _parser = parser;
        _engine = engine;
        // The Themes section is one more row in the editor grid (a list property), not a separate panel — see
        // BuildEditorSettings, which appends it after the reflected Engine / Build sections. Picking a theme
        // routes through OnThemeSelected (preview live + buffer) rather than persisting immediately.
        _themes = new ThemePropertyViewModel(theme, themeCreator, filePicker, OnThemeSelected);

        projects.ProjectChanged += _ => Dispatch.To(DispatchContext.UI, RefreshProjectSettings);
        // The project's AppSettings.json is lean (only its overrides); the full settings schema — graphics,
        // physics, etc., plus the plugins list's element_template — comes from the engine. Fetch it once the
        // engine connects and rebuild the project grid enriched. Until then the flat file renders as a fallback.
        session.StateChanged += OnSessionStateChanged;
        RefreshProjectSettings();
        BuildEditorSettings();

        // The panel may be opened while the engine is already connected (StateChanged only fires on a
        // transition), so fetch the schema now too if so.
        if (_engine.IsConnected)
            LoadSchemaThenRefreshAsync().FireAndForget();
    }

    private void OnSessionStateChanged(ConnectionState state)
    {
        if (state == ConnectionState.Connected)
            LoadSchemaThenRefreshAsync().FireAndForget();
    }

    private async Task LoadSchemaThenRefreshAsync()
    {
        await EnsureSettingsSchemaAsync().ContinueOnAnyContext();
        Dispatch.To(DispatchContext.UI, RefreshProjectSettings);
    }

    // The described AppSettings schema (attributed defaults), fetched once per session — it is
    // project-independent, so it is cached and reused for every project.
    private JObject? _settingsSchema;

    private async Task EnsureSettingsSchemaAsync()
    {
        if (_settingsSchema is not null)
            return;

        var result = await _engine.DescribeSettingsAsync(CancellationToken.None).ContinueOnAnyContext();
        if (result is { Success: true, Value: { } schema })
            _settingsSchema = schema;
    }

    [ObservableProperty]
    public partial bool HasProject { get; private set; }

    /// <summary>Settings search; filters both the Editor and Project grids by header or value.</summary>
    [ObservableProperty]
    public partial string Search { get; set; } = "";

    // Set while we persist project settings: our own save calls _projects.Reopen() to refresh the title and
    // notify listeners, which posts a ProjectChanged-driven RefreshProjectSettings back to us. Rebuilding the
    // grid we are editing would tear down the live rows and collapse any expanded list/struct (and drop edit
    // focus), so we swallow exactly that one self-induced refresh. The grid already shows the edited values.
    private bool _suppressProjectRefresh;

    private void RefreshProjectSettings()
    {
        if (_suppressProjectRefresh)
        {
            _suppressProjectRefresh = false;
            return;
        }

        var project = _projects.CurrentProject;
        HasProject = project is not null;
        _appJson = ReadAppJson(project);
        BuildProjectSettings();
    }

    // The same type-driven grid used by the entity inspector, pointed at the project's AppSettings.json
    // and the editor's own settings POCO. Leaf widgets mutate the backing JObject in place; the commit
    // closure persists it.

    private JObject? _editorSettingsJson;

    // The open project's app settings (AppSettings.json, asset type tbx::AppSettings) as the lean on-disk
    // JObject, or null when no project is open / the file can't be read. Reloaded by RefreshProjectSettings.
    private JObject? _appJson;

    // When the engine schema is available, the grid edits this merged document (the described defaults with
    // the on-disk values overlaid) in place, and _projectDefaults (a pristine clone of the schema) is the
    // reference the lean save diffs against. Both null on the flat fallback path (engine not connected).
    private JObject? _projectDoc;
    private JObject? _projectDefaults;

    // Dirty tracking: the live document the grid edits (schema path = _projectDoc, flat fallback = _appJson)
    // and a clone of it at the last save / (re)build. RecomputeDirty diffs working vs baseline for both the
    // editor and project grids. The editor's working copy is _editorSettingsJson.
    private JObject? _projectWorking;
    private JObject? _projectBaseline;
    private JObject? _editorBaseline;

    // The theme is previewed live but committed only on Save: _pendingTheme is the previewed selection,
    // _savedTheme the last-committed one. They differ exactly while the theme is a pending (dirty) change.
    private string _savedTheme = "";
    private string _pendingTheme = "";

    /// <summary>
    /// All of the current project's settings, edited through the generic property grid.
    /// </summary>
    public ObservableCollection<PropertyViewModel> ProjectSettingsProperties { get; } = [];

    /// <summary>
    /// All editor settings, edited through the generic property grid.
    /// </summary>
    public ObservableCollection<PropertyViewModel> EditorSettingsProperties { get; } = [];

    private void BuildProjectSettings()
    {
        ProjectSettingsProperties.Clear();
        _projectDoc = null;
        _projectDefaults = null;
        _projectWorking = null;
        _projectBaseline = null;
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
            _projectDefaults = (JObject)schema.DeepClone();
            _projectDoc = (JObject)schema.DeepClone();
            AppSettingsDocument.Merge(_projectDoc, app);
            document = _projectDoc;
        }

        foreach (var node in _parser.ParseProperties(document))
            ProjectSettingsProperties.Add(PropertyViewModelFactory.Create(node, OnEdited));

        // The grid edits `document` in place; snapshot it as the baseline this build's dirty state diffs against.
        _projectWorking = document;
        _projectBaseline = (JObject)document.DeepClone();
        RecomputeDirty();
    }

    private async Task SaveProjectSettingsAsync(CancellationToken ct)
    {
        if (_projects.CurrentProject is not { } project)
            return;

        // Schema path: persist a lean document (only the fields that differ from the engine defaults), exactly
        // as the engine's own omit-defaults serializer would. Fallback path: write the flat file as-is.
        var toWrite = _projectDoc is { } edited && _projectDefaults is { } defaults
            ? AppSettingsDocument.BuildLean(edited, defaults)
            : _appJson;
        if (toWrite is null)
            return;

        // Persist through the engine's generic asset save (it owns the AppSettings serializer) when connected;
        // fall back to writing the file directly so settings stay editable with no running engine.
        if (_engine.IsConnected)
        {
            var result = await _engine
                .SaveAssetAsync("AppSettings", project.AppSettingsPath, toWrite, ct)
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
                File.WriteAllText(project.AppSettingsPath, toWrite.ToString(Formatting.Indented));
            }
            catch (Exception exception)
            {
                _log.Error($"Failed to save project settings: {exception.Message}");
                return;
            }
        }

        _appJson = toWrite;
        // Reopen refreshes the display name + notifies listeners; suppress the resulting refresh of THIS
        // grid (posted via ProjectChanged) so editing a list/struct doesn't rebuild and collapse it.
        _suppressProjectRefresh = true;
        _projects.Reopen();
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

    private void BuildEditorSettings()
    {
        EditorSettingsProperties.Clear();
        // The theme lives outside the reflected JSON; baseline its tracking against the saved selection.
        _savedTheme = _editor.Theme.Active;
        _pendingTheme = _savedTheme;
        _editorSettingsJson = JObject.FromObject(_editor);
        // The theme selection renders as its own list-property section (appended below), and the
        // recent-projects bookkeeping is managed by the project system rather than hand-edited — drop both
        // groups from the reflected grid.
        _editorSettingsJson.Remove("Theme");
        _editorSettingsJson.Remove("Projects");
        // The reflected POCO loses the fact that the compiler is a fixed choice, so tag it as an enum
        // (with its options) here; the grid then renders it as a dropdown instead of a free-text box.
        TagEnum(_editorSettingsJson, "Build", "Compiler", [.. CMakeCompiler.CompilerChoices]);
        foreach (var node in _parser.ParseProperties(_editorSettingsJson))
            EditorSettingsProperties.Add(PropertyViewModelFactory.Create(node, OnEdited));

        // Each editable leaf gets a reset-to-default affordance + a "modified" indicator, comparing the live
        // value against a freshly-constructed EditorSettings (the defaults). Mirrors the inspector's revert,
        // but the default source here is the POCO rather than the engine.
        WireDefaults(EditorSettingsProperties, JObject.FromObject(new EditorSettings()));

        // Themes are the last section of the editor grid — a self-managing list property (its own add /
        // selection workflow), rendered with the same row chrome as the Engine / Build sections above. It
        // owns no settings JSON, so it sits outside the reflect/WireDefaults path (and the Save/Cancel buffer:
        // theme selection still applies live, as it always has).
        EditorSettingsProperties.Add(_themes);

        // Snapshot the just-built document as the baseline this grid's dirty state diffs against.
        _editorBaseline = (JObject)_editorSettingsJson.DeepClone();
        RecomputeDirty();
    }

    /// <summary>
    /// Walks the built rows alongside the default document (matched by raw key) and, for every editable leaf,
    /// wires <see cref="PropertyViewModel.ResetToDefault"/> to restore its default and keeps its
    /// <see cref="PropertyViewModel.IsModified"/> flag in sync with whether it currently differs from it.
    /// </summary>
    private static void WireDefaults(IEnumerable<PropertyViewModel> items, JObject defaults)
    {
        foreach (var viewModel in items)
        {
            var fallback = defaults[viewModel.RawName];
            if (viewModel is ObjectPropertyViewModel composite && fallback is JObject childDefaults)
            {
                WireDefaults(composite.Children, childDefaults);
            }
            else if (fallback is JValue value && !viewModel.IsReadOnly)
            {
                var def = value.DeepClone();
                viewModel.ResetToDefault = () => viewModel.ApplyValue(def);
                UpdateModified(viewModel, def);
                viewModel.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == "Value")
                        UpdateModified(viewModel, def);
                };
            }
        }
    }

    private static void UpdateModified(PropertyViewModel viewModel, JToken @default) =>
        viewModel.IsModified = viewModel.CurrentValue is { } current && !JToken.DeepEquals(current, @default);

    private void SaveEditorSettings()
    {
        if (_editorSettingsJson is null)
            return;

        // The live grid document carries typed wrappers (e.g. the compiler enum); collapse them on a
        // copy so PopulateObject sees plain values, leaving the editable document and its tokens intact.
        var plain = (JObject)_editorSettingsJson.DeepClone();
        FlattenTypedWrappers(plain);
        JsonConvert.PopulateObject(plain.ToString(), _editor);
        _editor.Save();
        // Engine + theme-selection changes here should take effect immediately.
        _theme.ApplySavedTheme();
    }

    // A leaf committed an edit (the live document was already mutated in place); re-derive the document's
    // dirty state. Nothing is written to disk until Save.
    private void OnEdited() => RecomputeDirty();

    // A theme was picked from the list: apply it live for preview (no persist) and buffer it as the pending
    // selection so it commits on Save and reverts on Cancel.
    private void OnThemeSelected(string name)
    {
        _theme.PreviewTheme(name);
        _pendingTheme = name;
        RecomputeDirty();
    }

    // Dirty when either grid's working document differs from its last-saved/built baseline, or the previewed
    // theme differs from the saved one.
    private void RecomputeDirty()
    {
        var editorDirty = _editorSettingsJson is { } editor && _editorBaseline is { } editorBaseline
            && !JToken.DeepEquals(editor, editorBaseline);
        var projectDirty = _projectWorking is { } project && _projectBaseline is { } projectBaseline
            && !JToken.DeepEquals(project, projectBaseline);
        var themeDirty = !string.Equals(_pendingTheme, _savedTheme, StringComparison.Ordinal);
        IsDirty = editorDirty || projectDirty || themeDirty;
    }

    // Re-snapshot both grids' working documents as the new baselines (after a successful Save).
    private void Rebaseline()
    {
        if (_editorSettingsJson is { } editor)
            _editorBaseline = (JObject)editor.DeepClone();
        if (_projectWorking is { } project)
            _projectBaseline = (JObject)project.DeepClone();
    }

    /// <summary>Commits both grids (editor POCO via C#, project settings via the engine), commits the pending
    /// theme, then re-baselines. Invoked by the base <see cref="DataPanel.SaveAsync"/> (the footer's Save).</summary>
    protected override async Task CommitAsync()
    {
        // Commit the previewed theme into the persisted editor settings before saving the POCO.
        _editor.Theme.Active = _pendingTheme;
        SaveEditorSettings();
        await SaveProjectSettingsAsync(CancellationToken.None).ContinueOnSameContext();
        _savedTheme = _pendingTheme;
        Rebaseline();
        RecomputeDirty();
    }

    /// <summary>Discards every unsaved edit: reverts the live theme preview to the saved theme, then rebuilds
    /// both grids from their unmodified sources (the editor POCO and the on-disk AppSettings.json), which
    /// re-baselines and clears the dirty flag. Invoked by the base <see cref="DataPanel.Cancel"/>.</summary>
    protected override void RevertChanges()
    {
        if (!string.Equals(_pendingTheme, _savedTheme, StringComparison.Ordinal))
            _theme.PreviewTheme(_savedTheme);

        BuildEditorSettings();
        // The flat-fallback grid edits _appJson in place, so re-read it from disk to undo those edits; the
        // schema path rebuilds _projectDoc fresh from the schema + this re-read file regardless.
        _appJson = ReadAppJson(_projects.CurrentProject);
        BuildProjectSettings();
    }

    /// <summary>
    /// Rewrites <paramref name="field"/> inside <paramref name="parent"/> as a typed enum wrapper
    /// (<c>{ "type": "enum", "value": …, "choices": […] }</c>) so the property grid shows a dropdown.
    /// <see cref="FlattenTypedWrappers"/> reverses this before the settings POCO is repopulated.
    /// </summary>
    private static void TagEnum(JObject root, string parent, string field, params string[] choices)
    {
        if (root[parent] is not JObject owner || owner[field] is not JValue current)
            return;

        owner[field] = new JObject
        {
            ["type"] = "enum",
            ["value"] = current,
            ["choices"] = new JArray(choices),
        };
    }

    /// <summary>
    /// Recursively replaces every typed wrapper (<c>{ "type", "value", … }</c>) with its inner value,
    /// turning a grid-editing document back into the plain JSON the settings POCO expects.
    /// </summary>
    private static void FlattenTypedWrappers(JToken token)
    {
        switch (token)
        {
            case JObject obj:
                foreach (var property in obj.Properties().ToList())
                {
                    if (property.Value is JObject wrapper
                        && wrapper["type"]?.Type == JTokenType.String
                        && wrapper["value"] is { } inner)
                        property.Value = inner.DeepClone();
                    else
                        FlattenTypedWrappers(property.Value);
                }

                break;

            case JArray array:
                foreach (var element in array)
                    FlattenTypedWrappers(element);
                break;
        }
    }
}
