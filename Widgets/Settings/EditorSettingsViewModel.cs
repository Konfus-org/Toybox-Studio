using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Services.Settings;
using Toybox.Studio.Services.Theming;
using Toybox.Studio.Utils;
using Toybox.Studio.Widgets.PropertyGrid;
using Toybox.Studio.Widgets.Theming;

namespace Toybox.Studio.Widgets.Settings;

/// <summary>
/// The Editor tab of the Settings panel: edits the editor's own <see cref="EditorSettings"/> POCO (Engine,
/// Build, Accessibility, …) through the generic property grid, plus the theme list. Buffered like the rest of
/// the panel — edits are held in a working JSON copy and committed only by the owning
/// <see cref="SettingsViewModel"/> on Save. The theme is the one preview-live value: picking a theme applies it
/// immediately but only persists on Save and reverts on Cancel.
/// </summary>
public sealed partial class EditorSettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settings;
    private readonly EditorSettings _editor;
    private readonly ThemeManager _theme;
    private readonly Locator _locator;
    private readonly JsonParser _parser;
    private readonly ThemePropertyViewModel _themes;

    // The grid edits this working copy of the editor POCO in place; _baseline is a clone of it at the last
    // save / (re)build, which dirty tracking diffs against.
    private JObject? _editorJson;
    private JObject? _baseline;

    // The theme is previewed live but committed only on Save: _pendingTheme is the previewed selection,
    // _savedTheme the last-committed one. They differ exactly while the theme is a pending (dirty) change.
    private string _savedTheme = "";
    private string _pendingTheme = "";

    public EditorSettingsViewModel(
        SettingsManager settings,
        ThemeManager theme,
        ThemeCreator themeCreator,
        FilePicker filePicker,
        Locator locator,
        JsonParser parser)
    {
        _settings = settings;
        _editor = settings.Settings;
        _theme = theme;
        _locator = locator;
        _parser = parser;
        // The Themes section is one more row in the editor grid (a list property), not a separate panel — see
        // Build, which appends it after the reflected Engine / Build sections. Picking a theme routes through
        // OnThemeSelected (preview live + buffer) rather than persisting immediately.
        _themes = new ThemePropertyViewModel(theme, themeCreator, filePicker, OnThemeSelected);

        Build();
    }

    /// <summary>Raised whenever <see cref="IsDirty"/> changes, so the owning panel can recompute aggregate dirty.</summary>
    public event Action? DirtyChanged;

    /// <summary>All editor settings, edited through the generic property grid.</summary>
    public ObservableCollection<PropertyViewModel> Properties { get; } = [];

    /// <summary>Whether either the reflected grid or the previewed theme differs from the last-saved baseline.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>
    /// Commits the editor POCO (and the pending theme) and re-baselines. Build-affecting settings (the C++
    /// compiler and the engine source path) each prompt for a recompile confirmation first; a declined field is
    /// reverted so the rest of the save still proceeds. Returns whether a confirmed build-affecting change means
    /// the engine must be rebuilt and relaunched (the owning panel performs the relaunch, after the project
    /// settings are also saved).
    /// </summary>
    public async Task<bool> CommitAsync()
    {
        // Detect build-affecting changes BEFORE committing/re-baselining (the baseline still reflects the
        // last-saved values). Parallel/Verbose edits never recompile.
        var compilerChanged = LeafChanged("Build", "Compiler");
        var enginePathChanged = LeafChanged("Engine", "SourcePath");
        var reverted = false;

        // Each build-affecting change needs an explicit "this recompiles" confirmation. On decline, revert
        // just that field so the rest of the save still proceeds.
        if (compilerChanged && !await ConfirmRecompileAsync("compiler").ContinueOnSameContext())
        {
            RevertLeaf("Build", "Compiler");
            compilerChanged = false;
            reverted = true;
        }

        if (enginePathChanged && !await ConfirmRecompileAsync("engine source path").ContinueOnSameContext())
        {
            RevertLeaf("Engine", "SourcePath");
            enginePathChanged = false;
            reverted = true;
        }

        // Commit the previewed theme into the persisted editor settings before saving the POCO.
        _editor.Theme.Active = _pendingTheme;
        await SaveAsync().ContinueOnSameContext();
        _savedTheme = _pendingTheme;

        // A changed engine path must reach the Locator (CompileProjectAsync reads _locator.EngineSourcePath,
        // not the raw setting). If it isn't a valid checkout, surface that and skip the relaunch.
        if (enginePathChanged)
        {
            var newPath = _editor.Engine.SourcePath;
            if (string.IsNullOrWhiteSpace(newPath) || !_locator.TrySetManually(newPath))
            {
                await Popups.ShowErrorAsync(
                    "Engine path",
                    $"'{newPath}' is not a valid engine source checkout; the engine was not relaunched.")
                    .ContinueOnSameContext();
                enginePathChanged = false;
            }
        }

        // A declined field was reverted in the working doc; rebuild the grid from the (reverted) POCO so the
        // row snaps back visually (this also re-baselines and recomputes dirty).
        if (reverted)
            Build();
        else
            Rebaseline();

        RecomputeDirty();
        return compilerChanged || enginePathChanged;
    }

    /// <summary>
    /// Discards every unsaved edit: reverts the live theme preview to the saved theme, then rebuilds the grid
    /// from the unmodified POCO (which re-baselines and clears the dirty flag).
    /// </summary>
    public void Revert()
    {
        if (!string.Equals(_pendingTheme, _savedTheme, StringComparison.Ordinal))
            _theme.PreviewTheme(_savedTheme);

        // The slider previewed motion live; re-broadcast the unchanged saved settings so the motion-token
        // listener restores the saved intensity now that the edit is discarded.
        _settings.NotifyChanged();

        Build();
    }

    private void Build()
    {
        Properties.Clear();
        // The theme lives outside the reflected JSON; baseline its tracking against the saved selection.
        _savedTheme = _editor.Theme.Active;
        _pendingTheme = _savedTheme;
        _editorJson = JObject.FromObject(_editor);
        // The theme selection renders as its own list-property section (appended below), and the
        // recent-projects bookkeeping is managed by the project system rather than hand-edited — drop both
        // groups from the reflected grid.
        _editorJson.Remove("Theme");
        _editorJson.Remove("Projects");
        // The reflected POCO loses the fact that the compiler is a fixed choice, so tag it as an enum
        // (with its options) here; the grid then renders it as a dropdown instead of a free-text box.
        TagEnum(_editorJson, "Build", "Compiler", [.. CMakeCompiler.CompilerChoices]);
        // Likewise, render the animation-intensity dial as a clay slider rather than a numeric field.
        TagView(_editorJson, "Accessibility", "AnimationIntensity", "intensitySlider");
        foreach (var node in _parser.ParseProperties(_editorJson))
            Properties.Add(PropertyViewModelFactory.Create(node, OnEdited));

        // Each editable leaf gets a reset-to-default affordance + a "modified" indicator, comparing the live
        // value against a freshly-constructed EditorSettings (the defaults). Mirrors the inspector's revert,
        // but the default source here is the POCO rather than the engine.
        WireDefaults(Properties, JObject.FromObject(new EditorSettings()));

        // Themes are the last section of the editor grid — a self-managing list property (its own add /
        // selection workflow), rendered with the same row chrome as the Engine / Build sections above. It
        // owns no settings JSON, so it sits outside the reflect/WireDefaults path (and the Save/Cancel buffer:
        // theme selection still applies live, as it always has).
        Properties.Add(_themes);

        // Snapshot the just-built document as the baseline this grid's dirty state diffs against.
        _baseline = (JObject)_editorJson.DeepClone();
        RecomputeDirty();
    }

    private async Task SaveAsync()
    {
        if (_editorJson is null)
            return;

        // The live grid document carries typed wrappers (e.g. the compiler enum); collapse them on a
        // copy so PopulateObject sees plain values, leaving the editable document and its tokens intact.
        var plain = (JObject)_editorJson.DeepClone();
        FlattenTypedWrappers(plain);
        JsonConvert.PopulateObject(plain.ToString(), _editor);
        // Flush to disk off the UI thread, then resume on it for the live theme apply below. The save raises
        // SettingsManager.Changed, which re-publishes the now-persisted animation intensity to the motion tokens
        // (the App-level listener owns that), so motion matches the saved value authoritatively.
        await _settings.SaveAsync().ContinueOnSameContext();
        // Engine + theme-selection changes here should take effect immediately.
        _theme.ApplySavedTheme();
    }

    // A leaf committed an edit (the live document was already mutated in place); re-derive the dirty state.
    private void OnEdited() => RecomputeDirty();

    // A theme was picked from the list: apply it live for preview (no persist) and buffer it as the pending
    // selection so it commits on Save and reverts on Cancel.
    private void OnThemeSelected(string name)
    {
        _theme.PreviewTheme(name);
        _pendingTheme = name;
        RecomputeDirty();
    }

    private void RecomputeDirty()
    {
        var gridDirty = _editorJson is { } editor && _baseline is { } baseline
            && !JToken.DeepEquals(editor, baseline);
        var themeDirty = !string.Equals(_pendingTheme, _savedTheme, StringComparison.Ordinal);
        var dirty = gridDirty || themeDirty;
        if (dirty == IsDirty)
            return;

        IsDirty = dirty;
        DirtyChanged?.Invoke();
    }

    private void Rebaseline()
    {
        if (_editorJson is { } editor)
            _baseline = (JObject)editor.DeepClone();
    }

    private static Task<bool> ConfirmRecompileAsync(string what) =>
        Popups.ConfirmAsync(
            "Recompile engine?",
            $"Changing the {what} will trigger a full engine recompile. Continue?",
            confirmText: "Recompile",
            cancelText: "Cancel");

    // Reads a leaf from an editor-settings doc, unwrapping a typed ({ type, value, … }) wrapper to its inner
    // value so an enum-tagged field (the compiler) compares like a plain one.
    private static JToken? ReadLeaf(JObject? doc, string section, string field)
    {
        if (doc?[section] is not JObject owner || owner[field] is not { } token)
            return null;
        return token is JObject wrapper && wrapper["value"] is { } inner ? inner : token;
    }

    private bool LeafChanged(string section, string field) =>
        !JToken.DeepEquals(ReadLeaf(_editorJson, section, field), ReadLeaf(_baseline, section, field));

    // Restores a single editor-settings field in the working doc to its baseline value (so a declined build
    // change isn't committed) while leaving every other edit intact. Handles the typed enum wrapper.
    private void RevertLeaf(string section, string field)
    {
        if (_editorJson?[section] is not JObject working
            || _baseline?[section] is not JObject baseline
            || baseline[field] is not { } baselineToken)
            return;

        if (working[field] is JObject wrapper && wrapper["value"] is not null
            && baselineToken is JObject baselineWrapper && baselineWrapper["value"] is { } inner)
            wrapper["value"] = inner.DeepClone();
        else
            working[field] = baselineToken.DeepClone();
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
            if (viewModel is ObjectPropertyViewModel structRow && fallback is JObject childDefaults)
            {
                // Wire the leaves first, then make the struct itself resettable — resetting it just resets all
                // its children. Its modified state is the children's aggregate (handled by the view-model).
                WireDefaults(structRow.Children, childDefaults);
                if (!structRow.IsReadOnly)
                    structRow.ResetToDefault = () => ResetChildren(structRow.Children);
            }
            else if (viewModel is ArrayPropertyViewModel listRow && fallback is JArray arrayDefault
                     && !listRow.IsReadOnly)
            {
                // A list resets to its whole default array (count + element values).
                var def = (JArray)arrayDefault.DeepClone();
                listRow.ResetToDefault = () => listRow.ApplyValue(def);
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

    // Resets a composite by reverting each of its children to default (each child was wired above).
    private static void ResetChildren(IEnumerable<PropertyViewModel> children)
    {
        foreach (var child in children)
            child.ResetToDefault?.Invoke();
    }

    private static void UpdateModified(PropertyViewModel viewModel, JToken @default) =>
        viewModel.IsModified = viewModel.CurrentValue is { } current && !JToken.DeepEquals(current, @default);

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
    /// Rewrites <paramref name="field"/> inside <paramref name="parent"/> as a typed wrapper carrying a custom
    /// editor <paramref name="view"/> (<c>{ "type", "value", "view" }</c>), so the grid routes it to the
    /// registered widget (e.g. the clay slider) instead of the type-driven default. The reflected POCO loses
    /// the [View] attribute, so the tag is re-applied here. <see cref="FlattenTypedWrappers"/> reverses it
    /// before the settings POCO is repopulated. <paramref name="type"/> is the value's JSON token (e.g. "double").
    /// </summary>
    private static void TagView(JObject root, string parent, string field, string view, string type = "double")
    {
        if (root[parent] is not JObject owner || owner[field] is not JValue current)
            return;

        owner[field] = new JObject
        {
            ["type"] = type,
            ["value"] = current,
            ["view"] = view,
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
