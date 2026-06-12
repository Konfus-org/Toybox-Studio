using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services;
using Toybox.Studio.Widgets.PropertyGrid;

namespace Toybox.Studio.Shell;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly Settings _settings;
    private readonly ThemeManager _theme;
    private readonly EngineLocator _locator;
    private readonly ProjectManager _projects;
    private readonly FilePicker _filePicker;
    private readonly Logger _log;
    private readonly EngineJsonParser _parser;

    public SettingsViewModel(
        Settings settings,
        ThemeManager theme,
        EngineLocator locator,
        ProjectManager projects,
        FilePicker filePicker,
        Logger log,
        EngineJsonParser parser)
    {
        _settings = settings;
        _theme = theme;
        _locator = locator;
        _projects = projects;
        _filePicker = filePicker;
        _log = log;
        _parser = parser;

        ConnectTimeoutSeconds = settings.Editor.Engine.ConnectTimeoutSeconds;
        HideEngineWindow = settings.Editor.Engine.HideEngineWindow;
        RestartOnCrash = settings.Editor.Engine.RestartOnCrash;
        EnginePath = locator.EngineSourcePath ?? "(not found)";

        locator.EngineChanged += path =>
            Dispatch.To(DispatchContext.UI, () => EnginePath = path ?? "(not found)");
        projects.ProjectChanged += _ => Dispatch.To(DispatchContext.UI, RefreshAppSettings);
        RefreshAppSettings();
        BuildEditorSettingsGrid();
    }

    //// THEME ////
    // Theme selection lives in the editor-settings grid (the Variant and per-variant theme pickers,
    // tagged in BuildEditorSettingsGrid). Authoring a new theme happens in the modal Theme Creator;
    // built-in themes are non-editable.

    /// <summary>
    /// Raised when the user asks to author a new theme; the view opens the modal Theme Creator and
    /// calls <see cref="RefreshThemes"/> when it closes.
    /// </summary>
    public event Action? ThemeCreatorRequested;

    /// <summary>
    /// The theme manager, exposed so the view can launch the Theme Creator with it.
    /// </summary>
    public ThemeManager Theme => _theme;

    [RelayCommand]
    private void CreateTheme() => ThemeCreatorRequested?.Invoke();

    /// <summary>
    /// Rebuilds the editor-settings grid so the theme pickers pick up a newly created theme and the
    /// current selection.
    /// </summary>
    public void RefreshThemes() => BuildEditorSettingsGrid();

    [RelayCommand]
    private void OpenThemesFolder()
    {
        Directory.CreateDirectory(_theme.ThemesDirectory);
        Process.Start(new ProcessStartInfo(_theme.ThemesDirectory) { UseShellExecute = true });
    }

    //// ENGINE ////

    [ObservableProperty]
    public partial string EnginePath { get; private set; }

    [ObservableProperty]
    public partial int ConnectTimeoutSeconds { get; set; }

    [ObservableProperty]
    public partial bool HideEngineWindow { get; set; }

    [ObservableProperty]
    public partial bool RestartOnCrash { get; set; }

    partial void OnConnectTimeoutSecondsChanged(int value)
    {
        _settings.Editor.Engine.ConnectTimeoutSeconds = value;
        _settings.Save();
    }

    partial void OnHideEngineWindowChanged(bool value)
    {
        _settings.Editor.Engine.HideEngineWindow = value;
        _settings.Save();
    }

    partial void OnRestartOnCrashChanged(bool value)
    {
        _settings.Editor.Engine.RestartOnCrash = value;
        _settings.Save();
    }

    [RelayCommand]
    private async Task BrowseEngineAsync()
    {
        var path = await _filePicker.PickFolderAsync("Locate the Toybox Engine source folder")
            .ContinueOnAnyContext();
        if (path is null)
            return;

        if (!_locator.TrySetManually(path))
            _log.Error($"'{path}' is not a Toybox Engine source folder.");
    }

    //// APP ////

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAppSettingsCommand))]
    public partial bool HasProject { get; private set; }

    [ObservableProperty]
    public partial string AppName { get; set; } = "";

    [ObservableProperty]
    public partial string PluginsText { get; set; } = "";

    [RelayCommand(CanExecute = nameof(HasProject))]
    private void SaveAppSettings()
    {
        var plugins = PluginsText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (!_settings.TrySaveAppSettings(_projects.CurrentProject, AppName, plugins, out var error))
        {
            _log.Error(error ?? "Failed to save app settings.");
        }
        else
        {
            _projects.Reopen(); // refresh display name + notify listeners
            _log.Info("App settings saved.");
        }
    }

    private void RefreshAppSettings()
    {
        var appSettings = _settings.ReadAppSettings(_projects.CurrentProject);
        HasProject = appSettings is not null;
        AppName = appSettings?.Name ?? "";
        PluginsText = appSettings is null ? "" : string.Join('\n', appSettings.Plugins);
        BuildAppSettingsGrid();
    }

    //// GENERIC PROPERTY GRIDS ////
    // The same type-driven grid used by the entity inspector, pointed at the project's AppSettings.json
    // and the editor's own settings POCO. Leaf widgets mutate the backing JObject in place; the commit
    // closure persists it.

    private JObject? _appSettingsJson;
    private JObject? _editorSettingsJson;

    /// <summary>
    /// All of the current project's app settings, edited through the generic property grid.
    /// </summary>
    public ObservableCollection<PropertyViewModelBase> AppSettingsProperties { get; } = [];

    /// <summary>
    /// All editor settings, edited through the generic property grid.
    /// </summary>
    public ObservableCollection<PropertyViewModelBase> EditorSettingsProperties { get; } = [];

    private void BuildAppSettingsGrid()
    {
        AppSettingsProperties.Clear();
        _appSettingsJson = _settings.ReadAppSettingsJson(_projects.CurrentProject);
        if (_appSettingsJson is null)
            return;

        foreach (var node in _parser.ParseProperties(_appSettingsJson))
            AppSettingsProperties.Add(PropertyViewModelFactory.Create(node, SaveAppSettingsGrid));
    }

    private void SaveAppSettingsGrid()
    {
        if (_appSettingsJson is null)
            return;

        if (!_settings.TrySaveAppSettingsJson(_projects.CurrentProject, _appSettingsJson, out var error))
            _log.Error(error ?? "Failed to save app settings.");
        else
            _projects.Reopen(); // refresh display name + notify listeners
    }

    private void BuildEditorSettingsGrid()
    {
        EditorSettingsProperties.Clear();
        _editorSettingsJson = JObject.FromObject(_settings.Editor);
        // The reflected POCO loses the fact that the compiler is a fixed choice, so tag it as an enum
        // (with its options) here; the grid then renders it as a dropdown instead of a free-text box.
        TagEnum(_editorSettingsJson, "Build", "Compiler", "Auto", "MSVC", "Clang");
        // Theme selection: variant is a Dark/Light enum; the two per-variant theme names render as
        // [View("themePicker")] dropdowns, populated with the themes available for each variant.
        TagViewsFromAttributes(_editorSettingsJson, _settings.Editor);
        TagEnum(_editorSettingsJson, "Theme", "Variant", "Dark", "Light");
        InjectChoices(_editorSettingsJson, "Theme", "DarkTheme", _theme.ThemeNamesFor(ThemeMode.Dark));
        InjectChoices(_editorSettingsJson, "Theme", "LightTheme", _theme.ThemeNamesFor(ThemeMode.Light));
        foreach (var node in _parser.ParseProperties(_editorSettingsJson))
            EditorSettingsProperties.Add(PropertyViewModelFactory.Create(node, SaveEditorSettingsGrid));
    }

    /// <summary>
    /// Walks the reflected settings POCO and, for every property carrying a <see cref="ViewAttribute"/>,
    /// rewrites the matching JSON field as a typed wrapper carrying <c>$view</c> so the property grid
    /// routes it to a custom control. Recurses into nested setting objects.
    /// </summary>
    private static void TagViewsFromAttributes(JObject json, object poco)
    {
        foreach (var property in poco.GetType().GetProperties())
        {
            var value = property.GetValue(poco);
            var view = property.GetCustomAttribute<ViewAttribute>();
            if (view is not null && json[property.Name] is JValue current)
            {
                json[property.Name] = new JObject
                {
                    ["type"] = "string",
                    ["value"] = current,
                    ["view"] = view.Name,
                };
            }
            else if (json[property.Name] is JObject child
                     && value is not null
                     && property.PropertyType is { IsClass: true } type
                     && type != typeof(string))
            {
                TagViewsFromAttributes(child, value);
            }
        }
    }

    /// <summary>
    /// Adds a <c>$choices</c> list to an already-wrapped field (e.g. a themePicker), so its dropdown
    /// lists the given options. No-op when the field is absent or not a typed wrapper.
    /// </summary>
    private static void InjectChoices(JObject root, string parent, string field, IReadOnlyList<string> choices)
    {
        if (root[parent] is JObject owner && owner[field] is JObject wrapper)
            wrapper["choices"] = new JArray(choices);
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

    private void SaveEditorSettingsGrid()
    {
        if (_editorSettingsJson is null)
            return;

        // The live grid document carries typed wrappers (e.g. the compiler enum); collapse them on a
        // copy so PopulateObject sees plain values, leaving the editable document and its tokens intact.
        var plain = (JObject)_editorSettingsJson.DeepClone();
        FlattenTypedWrappers(plain);
        JsonConvert.PopulateObject(plain.ToString(), _settings.Editor);
        _settings.Save();
        // Engine + theme-selection changes here should take effect immediately.
        _theme.ApplySavedTheme();
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
