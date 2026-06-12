using System.Collections.ObjectModel;
using System.Diagnostics;
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

    // Guards the cascade when we programmatically change the variant/theme selection.
    private bool _suppressThemeEvents;

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

        SelectedVariant = _theme.Variant;
        AvailableThemes = _theme.ThemeNamesFor(SelectedVariant);
        SelectedTheme = _theme.Active.Name;
        LoadEditorFrom(_theme.Active);

        ConnectTimeoutSeconds = settings.Editor.Engine.ConnectTimeoutSeconds;
        HideEngineWindow = settings.Editor.Engine.HideEngineWindow;
        RestartOnCrash = settings.Editor.Engine.RestartOnCrash;
        EnginePath = locator.EngineSourcePath ?? "(not found)";

        locator.EngineChanged += path =>
            Dispatch.To(DispatchContext.UI, () => EnginePath = path ?? "(not found)");
        projects.ProjectChanged += _ => Dispatch.To(DispatchContext.UI, RefreshAppSettings);
        RefreshAppSettings();
        BuildEditorSettingsGrid();
        BuildThemeGrid();
    }

    //// THEME ////

    public IReadOnlyList<string> Variants => _theme.VariantNames;

    [ObservableProperty]
    public partial string SelectedVariant { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> AvailableThemes { get; private set; }

    [ObservableProperty]
    public partial string SelectedTheme { get; set; }

    [ObservableProperty]
    public partial string FontFamily { get; set; } = "";

    [ObservableProperty]
    public partial double FontSize { get; set; }

    [ObservableProperty]
    public partial string MonospaceFamily { get; set; } = "";

    [ObservableProperty]
    public partial double CornerRadius { get; set; }

    [ObservableProperty]
    public partial string PrimaryHex { get; set; } = "";

    [ObservableProperty]
    public partial string SecondaryHex { get; set; } = "";

    [ObservableProperty]
    public partial string TertiaryHex { get; set; } = "";

    [ObservableProperty]
    public partial string ErrorHex { get; set; } = "";

    [ObservableProperty]
    public partial string WarningHex { get; set; } = "";

    [ObservableProperty]
    public partial string InfoHex { get; set; } = "";

    [ObservableProperty]
    public partial string SuccessHex { get; set; } = "";

    [ObservableProperty]
    public partial string BackgroundHex { get; set; } = "";

    [ObservableProperty]
    public partial string SurfaceHex { get; set; } = "";

    [ObservableProperty]
    public partial string TextHex { get; set; } = "";

    partial void OnSelectedVariantChanged(string value)
    {
        if (_suppressThemeEvents)
            return;

        _theme.SetVariant(value);
        _suppressThemeEvents = true;
        AvailableThemes = _theme.ThemeNamesFor(value);
        SelectedTheme = _theme.Active.Name;
        _suppressThemeEvents = false;
        LoadEditorFrom(_theme.Active);
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (_suppressThemeEvents || string.IsNullOrEmpty(value))
            return;

        _theme.SetActiveTheme(value);
        LoadEditorFrom(_theme.Active);
    }

    /// <summary>
    /// Writes the editor fields back to the active theme's Theme.json and applies it.
    /// </summary>
    [RelayCommand]
    private void SaveTheme()
    {
        _theme.SaveTheme(BuildThemeFromEditor());
        LoadEditorFrom(_theme.Active);
    }

    [RelayCommand]
    private void ReloadThemes()
    {
        _theme.Reload();
        _suppressThemeEvents = true;
        SelectedVariant = _theme.Variant;
        AvailableThemes = _theme.ThemeNamesFor(SelectedVariant);
        SelectedTheme = _theme.Active.Name;
        _suppressThemeEvents = false;
        LoadEditorFrom(_theme.Active);
    }

    [RelayCommand]
    private void OpenThemesFolder()
    {
        Directory.CreateDirectory(_theme.ThemesDirectory);
        Process.Start(new ProcessStartInfo(_theme.ThemesDirectory) { UseShellExecute = true });
    }

    private void LoadEditorFrom(Theme theme)
    {
        FontFamily = theme.Font.Family;
        FontSize = theme.Font.Size;
        MonospaceFamily = theme.Font.Monospace;
        CornerRadius = theme.CornerRadius;
        PrimaryHex = theme.Colors.Primary;
        SecondaryHex = theme.Colors.Secondary;
        TertiaryHex = theme.Colors.Tertiary;
        ErrorHex = theme.Colors.Error;
        WarningHex = theme.Colors.Warning;
        InfoHex = theme.Colors.Info;
        SuccessHex = theme.Colors.Success;
        BackgroundHex = theme.Colors.Background;
        SurfaceHex = theme.Colors.Surface;
        TextHex = theme.Colors.Text;
    }

    private Theme BuildThemeFromEditor() => new()
    {
        Name = _theme.Active.Name,
        Variant = _theme.Active.Variant,
        CornerRadius = CornerRadius,
        Font = new ThemeFont { Family = FontFamily, Size = FontSize, Monospace = MonospaceFamily },
        Colors = new ThemePalette
        {
            Primary = PrimaryHex,
            Secondary = SecondaryHex,
            Tertiary = TertiaryHex,
            Error = ErrorHex,
            Warning = WarningHex,
            Info = InfoHex,
            Success = SuccessHex,
            Background = BackgroundHex,
            Surface = SurfaceHex,
            Text = TextHex,
        },
    };

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
    private JObject? _themeJson;

    /// <summary>
    /// All of the current project's app settings, edited through the generic property grid.
    /// </summary>
    public ObservableCollection<PropertyViewModelBase> AppSettingsProperties { get; } = [];

    /// <summary>
    /// All editor settings, edited through the generic property grid.
    /// </summary>
    public ObservableCollection<PropertyViewModelBase> EditorSettingsProperties { get; } = [];

    /// <summary>
    /// The active theme's palette/fonts, edited through the generic property grid.
    /// </summary>
    public ObservableCollection<PropertyViewModelBase> ThemeProperties { get; } = [];

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
        foreach (var node in _parser.ParseProperties(_editorSettingsJson))
            EditorSettingsProperties.Add(PropertyViewModelFactory.Create(node, SaveEditorSettingsGrid));
    }

    /// <summary>
    /// Rewrites <paramref name="field"/> inside <paramref name="parent"/> as a typed enum wrapper
    /// (<c>{ "$type": "enum", "$value": …, "$choices": […] }</c>) so the property grid shows a dropdown.
    /// <see cref="FlattenTypedWrappers"/> reverses this before the settings POCO is repopulated.
    /// </summary>
    private static void TagEnum(JObject root, string parent, string field, params string[] choices)
    {
        if (root[parent] is not JObject owner || owner[field] is not JValue current)
            return;

        owner[field] = new JObject
        {
            ["$type"] = "enum",
            ["$value"] = current,
            ["$choices"] = new JArray(choices),
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
    /// Recursively replaces every typed wrapper (<c>{ "$type", "$value", … }</c>) with its inner value,
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
                        && wrapper["$type"]?.Type == JTokenType.String
                        && wrapper["$value"] is { } inner)
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

    private void BuildThemeGrid()
    {
        ThemeProperties.Clear();
        _themeJson = JObject.FromObject(_theme.Active);
        foreach (var node in _parser.ParseProperties(_themeJson))
            ThemeProperties.Add(PropertyViewModelFactory.Create(node, SaveThemeGrid));
    }

    private void SaveThemeGrid()
    {
        if (_themeJson is null)
            return;

        var theme = _themeJson.ToObject<Theme>();
        if (theme is not null)
            _theme.SaveTheme(theme);
    }
}
