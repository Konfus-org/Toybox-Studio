using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.Theming;
using Toybox.Studio.Utils;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Widgets.PropertyGrid;

namespace Toybox.Studio.Widgets.Theming;

/// <summary>
/// The Themes section, modelled as a list property injected straight into the editor-settings grid alongside
/// the Engine and Build sections (rather than a bolted-on panel beneath it). It renders with the same banded
/// parent header and depth-1 child rows as those object sections, so it reads as one more section of the one
/// grid. Each row ticks a theme to apply it; the header's "+" is the list's add affordance, offering
/// "Create New…" (the Theme Creator) or "From Disk…" (import a .json theme, copied into the themes folder).
/// </summary>
public sealed partial class ThemePropertyViewModel : PropertyViewModel
{
    private readonly ThemeManager _themes;
    private readonly ThemeCreator _creator;
    private readonly FilePicker _files;
    private readonly Action<string> _onSelect;

    // Expanded by default — the theme list is short and the active selection should be visible at a glance.
    private bool _isExpanded = true;

    private string _summary = "";

    public ThemePropertyViewModel(
        ThemeManager themes, ThemeCreator creator, FilePicker files, Action<string> onSelect)
        : base(new PropertyNode { Name = "Themes", Label = "Themes", Type = "array" })
    {
        _themes = themes;
        _creator = creator;
        _files = files;
        _onSelect = onSelect;
        _themes.ThemeChanged += () => Dispatch.To(DispatchContext.UI, Refresh);
        Refresh();
    }

    public override bool IsComposite => true;

    public override bool HasChildren => true;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>Every loaded theme, as a selectable child row.</summary>
    public ObservableCollection<ThemeItemViewModel> Items { get; } = [];

    /// <summary>The collapsed-state count shown beside the header's add button (e.g. "3 themes").</summary>
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    // The theme rows are the searchable children, so a settings search can match a theme by name and keep the
    // Themes section in view.
    protected override IEnumerable<PropertyViewModel> FilterChildren => Items;

    /// <summary>Authors a brand-new theme via the Theme Creator, then picks up the result in the list.</summary>
    [RelayCommand]
    private async Task CreateTheme()
    {
        await _creator.CreateAsync().ContinueOnSameContext();
        Refresh();
    }

    /// <summary>
    /// Imports a theme .json from disk: copies it into the themes folder under a free name, then refreshes
    /// the list. Surfaces a dialog if the chosen file isn't a valid theme.
    /// </summary>
    [RelayCommand]
    private async Task ImportTheme()
    {
        var path = await _files
            .PickFileAsync("Import a theme", new FilePickerFileType("Theme files") { Patterns = ["*.json"] })
            .ContinueOnSameContext();
        if (path is null)
            return;

        if (!_themes.TryImportTheme(path, out var error, out _))
        {
            await Popups.ShowErrorAsync("Couldn't import theme", error ?? "That file isn't a valid theme.")
                .ContinueOnSameContext();
            return;
        }

        Refresh();
    }

    private void Refresh()
    {
        Items.Clear();
        var active = _themes.Active.Name;
        foreach (var theme in _themes.Themes)
            Items.Add(new ThemeItemViewModel(
                theme.Name,
                string.Equals(theme.Name, active, StringComparison.OrdinalIgnoreCase),
                Depth + 1,
                _onSelect));

        Summary = $"{Items.Count} theme{(Items.Count == 1 ? "" : "s")}";
    }
}
