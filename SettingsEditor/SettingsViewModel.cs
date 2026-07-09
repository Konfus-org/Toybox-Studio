using Toybox.Studio.AssetOwners;
using Toybox.Studio.Behaviors.Animations;
using Toybox.Studio.Events;
using Toybox.Studio.Keybindings;
using Toybox.Studio.KeybindingsEditor;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.Settings;
using Toybox.Studio.Utils;

namespace Toybox.Studio.SettingsEditor;

/// <summary>
/// The Settings panel's state: an <see cref="AssetOwnerViewModel"/> whose body is two tabbed
/// property grids over drafts — <b>Editor</b> (the editor settings draft, whose Keybindings section
/// is the editor keymap surfaced as plain data: the reflection grid renders it like any other list,
/// each chord through the chord capture editor; the sections commit through
/// <see cref="SettingsManager.ApplyEditorDraftAsync"/> and the keybindings through
/// <see cref="EditorKeymap.ApplyKeybindingsAsync"/>) and <b>Project</b> (the open project's
/// <see cref="AppSettings"/> mirror via an <see cref="AppSettingsDraft"/>, landed as whole-record
/// pushes and persisted through the asset's save; empty while no engine connection carries the
/// mirror). One Save lands every edited tab; closing without saving discards the drafts. The panel's
/// own lifetime is the workspace's business, hooked to the dock through <see cref="IDockAware"/>.
/// </summary>
public sealed class SettingsViewModel : AssetOwnerViewModel, IDockAware,
    IEventHandler<AppSettingsChanged>
{
    private readonly SettingsManager _settings;
    private readonly EditorKeymap _keymap;

    private EditorSettings? _editorDraft;
    private AppSettingsDraft? _projectDraft;
    private bool _editorEdited;
    private bool _projectEdited;

    public SettingsViewModel(SettingsManager settings, EditorKeymap keymap, EventDispatcher events)
    {
        _settings = settings;
        _keymap = keymap;

        EditorGrid = new PropertyGridViewModel(new ReflectionPropertyNodeFactory(new ChordValueEditor()));
        EditorGrid.Edited += OnEditorEdited;
        ProjectGrid = new PropertyGridViewModel(new ReflectionPropertyNodeFactory());
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

    /// <summary>Builds fresh drafts from the live settings/keymap and shows them in the grids.</summary>
    public void Open()
    {
        _editorDraft = _settings.CreateEditorDraft();
        // The keybindings section isn't part of the settings file — the keymap fills it here and
        // commits it on save.
        _editorDraft.Keybindings = _keymap.CreateKeybindingDrafts();
        EditorGrid.Show(_editorDraft);
        RefreshProjectDraft();
        ClearDirty();
    }

    /// <summary>Discards the drafts (the panel closed) and reverts anything that live-previewed.</summary>
    public void Close()
    {
        EditorGrid.Show(null);
        ProjectGrid.Show(null);
        _editorDraft = null;
        _projectDraft = null;
        ClearDirty();
        MotionTokens.Publish(_settings.Editor.Accessibility.AnimationIntensity);
    }

    void IDockAware.OnDockOpened() => Open();

    void IDockAware.OnDockClosed() => Close();

    /// <summary>The mirror swapped (engine connected/disconnected/reloaded): rebuild the project tab
    /// over the new instance — a swap invalidates the old draft wholesale.</summary>
    public void Handle(in AppSettingsChanged evt) => Dispatch.To(DispatchContext.UI, () =>
    {
        OnPropertyChanged(nameof(IsProjectAvailable));
        if (_editorDraft is not null)
            RefreshProjectDraft();
    });

    protected override async Task<Result> PersistAsync()
    {
        if (_editorEdited && _editorDraft is not null)
        {
            await _settings.ApplyEditorDraftAsync(_editorDraft).ContinueOnSameContext();
            await _keymap.ApplyKeybindingsAsync(_editorDraft.Keybindings).ContinueOnSameContext();
            _editorEdited = false;
        }

        if (_projectEdited && _projectDraft is not null && _settings.App is { } app)
        {
            _projectDraft.ApplyTo(app);
            var save = await app.SaveAsync().ContinueOnSameContext();
            if (!save)
                return save;
            _projectEdited = false;
        }

        return Result.Ok();
    }

    private void RefreshProjectDraft()
    {
        _projectDraft = _settings.App is { } app ? AppSettingsDraft.From(app) : null;
        ProjectGrid.Show(_projectDraft);
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

        // Live-preview the motion dial: the editor's micro-animations follow the draft value while the
        // window is open (Close/Save settle it).
        if (_editorDraft is not null)
            MotionTokens.Publish(_editorDraft.Accessibility.AnimationIntensity);
    }

    private void OnProjectEdited()
    {
        _projectEdited = true;
        IsDirty = true;
    }
}
