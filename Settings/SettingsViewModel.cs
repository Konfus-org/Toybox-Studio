using Toybox.Studio.AssetOwners;
using Toybox.Studio.Behaviors.Animations;
using Toybox.Studio.Events;
using Toybox.Studio.Keybindings;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Composition;

namespace Toybox.Studio.Settings;

/// <summary>
/// The Settings panel's state: an <see cref="AssetOwnerViewModel"/> whose body is two tabbed
/// property grids — <b>Editor</b> (the live editor settings, edited in place, whose Keybindings section
/// is the editor keymap surfaced as plain data: the reflection grid renders it like any other list,
/// each chord through the chord capture editor; the sections commit through
/// <see cref="SettingsManager.ApplyAsync"/> and the keybindings through
/// <see cref="EditorKeymap.ApplyKeybindingsAsync"/>) and <b>Project</b> (the open project's live
/// <see cref="AppSettings"/> mirror, edited directly — the grid's copy-on-write reassigns each section
/// record, so edits push live like any asset's and persist through the asset's own
/// <see cref="Asset.SaveAsync"/>; empty while no engine connection carries the mirror). Both tabs edit
/// the live objects directly; Save writes them to disk. The panel's own lifetime is the workspace's
/// business, hooked to the dock through <see cref="IDockAware"/>.
/// </summary>
public sealed class SettingsViewModel : AssetOwnerViewModel, IDockAware,
    IEventHandler<AppSettingsChanged>
{
    private readonly SettingsManager _settings;
    private readonly EditorKeymap _keymap;

    private bool _open;
    private bool _editorEdited;
    private bool _projectEdited;

    public SettingsViewModel(
        SettingsManager settings, EditorKeymap keymap, EventDispatcher events, ViewModelFactory viewModels)
    {
        _settings = settings;
        _keymap = keymap;

        EditorGrid = viewModels.Create<PropertyGridViewModel>(
            new ReflectionPropertyNodeFactory(viewModels, new ChordValueEditor(viewModels)));
        EditorGrid.Edited += OnEditorEdited;
        ProjectGrid = viewModels.Create<PropertyGridViewModel>(new ReflectionPropertyNodeFactory(
            viewModels, new HandlePickerValueEditor(viewModels), new SizeValueEditor(viewModels)));
        ProjectGrid.Edited += OnProjectEdited;

        // The panel is a workspace-owned singleton alive for the app's lifetime, so it registers once
        // and never unregisters.
        events.RegisterAll(this);
    }

    public PropertyGridViewModel EditorGrid { get; }

    public PropertyGridViewModel ProjectGrid { get; }

    /// <summary>Whether the project tab has settings to edit — the mirror is live only while the
    /// engine connection carries the project's AppSettings.json.</summary>
    public bool IsProjectAvailable => _settings.App is not null;

    protected override string DisplayName => "Settings";

    /// <summary>The owner chrome's content is this view-model itself; the view's DataTemplate
    /// resolves it to the tabbed body.</summary>
    public override object? Body => this;

    /// <summary>Shows the live editor settings/keymap and project mirror in the grids.</summary>
    public void Open()
    {
        _open = true;
        // The keybindings section isn't part of the settings file — the keymap fills it here and
        // commits it on save.
        _settings.Editor.Keybindings = _keymap.CreateKeybindings();
        EditorGrid.Show(_settings.Editor);
        ShowProjectSettings();
        ClearDirty();
    }

    /// <summary>The panel closed: drops the grids and settles the live-previewed motion dial.</summary>
    public void Close()
    {
        _open = false;
        EditorGrid.Show(null);
        ProjectGrid.Show(null);
        ClearDirty();
        MotionTokens.Publish(_settings.Editor.Accessibility.AnimationIntensity);
    }

    void IDockAware.OnDockOpened() => Open();

    void IDockAware.OnDockClosed() => Close();

    /// <summary>The mirror swapped (engine connected/disconnected/reloaded): rebuild the project tab
    /// over the new instance — a swap invalidates the shown grid wholesale.</summary>
    public void Handle(in AppSettingsChanged evt) => Dispatch.To(DispatchContext.UI, () =>
    {
        OnPropertyChanged(nameof(IsProjectAvailable));
        if (_open)
            ShowProjectSettings();
    });

    protected override async Task<Result> PersistAsync()
    {
        if (_editorEdited)
        {
            // The editor grid spans both the user-global sections and the project-scoped ones (Build,
            // Gizmos, EditorAssetSettings), so an edit here may touch either — write both files.
            await _settings.ApplyAsync().ContinueOnSameContext();
            await _settings.ApplyProjectAsync().ContinueOnSameContext();
            await _keymap.ApplyKeybindingsAsync(_settings.Editor.Keybindings).ContinueOnSameContext();
            _editorEdited = false;
        }

        // Project edits have already pushed live to the engine; the save is what writes them to disk.
        if (_projectEdited && _settings.App is { } app)
        {
            var save = await app.SaveAsync().ContinueOnSameContext();
            if (!save)
                return save;
            _projectEdited = false;
        }

        return Result.Ok();
    }

    private void ShowProjectSettings()
    {
        ProjectGrid.Show(_settings.App);
        _projectEdited = false;
    }

    private void ClearDirty()
    {
        _editorEdited = false;
        _projectEdited = false;
        IsDirty = false;
    }

    private void OnEditorEdited()
    {
        _editorEdited = true;
        IsDirty = true;

        // Live-preview the motion dial: the editor's micro-animations follow the live value while the
        // window is open (Close/Save settle it).
        MotionTokens.Publish(_settings.Editor.Accessibility.AnimationIntensity);
    }

    private void OnProjectEdited()
    {
        _projectEdited = true;
        IsDirty = true;
    }
}
