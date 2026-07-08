using Toybox.Studio.AssetOwners;
using Toybox.Studio.Behaviors.Animations;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.Settings;
using Toybox.Studio.Utils;

namespace Toybox.Studio.SettingsEditor;

/// <summary>
/// The Settings panel's state: an <see cref="AssetOwnerViewModel"/> whose body is a property grid
/// (reflection factory) over a draft copy of the editor settings, so edits can preview live — the
/// animation-intensity tokens republish as the value moves — but land in the real
/// <see cref="EditorSettings"/> only on Save, which commits through
/// <see cref="SettingsManager.ApplyEditorDraftAsync"/> (and so dispatches the usual
/// <c>EditorSettingsChanged</c>). Closing without saving discards the draft and reverts the preview.
/// The panel's own lifetime is the workspace's business; this only models open / edit / save / close,
/// hooked to the dock through <see cref="IDockAware"/>.
/// </summary>
public sealed class SettingsViewModel : AssetOwnerViewModel, IDockAware
{
    private readonly SettingsManager _settings;
    private EditorSettings? _draft;

    public SettingsViewModel(SettingsManager settings)
    {
        _settings = settings;
        Grid = new PropertyGridViewModel(new ReflectionPropertyNodeFactory());
        Grid.Edited += OnEdited;
    }

    public PropertyGridViewModel Grid { get; }

    protected override string DisplayName => "Settings";

    public override object? Body => Grid;

    /// <summary>Builds a fresh draft from the current settings and shows it in the grid.</summary>
    public void Open()
    {
        _draft = _settings.CreateEditorDraft();
        Grid.Show(_draft);
        IsDirty = false;
    }

    /// <summary>Discards the draft (the panel closed) and reverts anything that live-previewed.</summary>
    public void Close()
    {
        Grid.Show(null);
        _draft = null;
        IsDirty = false;
        MotionTokens.Publish(_settings.Editor.Accessibility.AnimationIntensity);
    }

    void IDockAware.OnDockOpened() => Open();

    void IDockAware.OnDockClosed() => Close();

    protected override async Task<Result> PersistAsync()
    {
        if (_draft is null)
            return Result.Ok();

        await _settings.ApplyEditorDraftAsync(_draft).ContinueOnSameContext();
        return Result.Ok();
    }

    private void OnEdited()
    {
        IsDirty = true;

        // Live-preview the motion dial: the editor's micro-animations follow the draft value while the
        // window is open (Close/Save settle it).
        if (_draft is not null)
            MotionTokens.Publish(_draft.Accessibility.AnimationIntensity);
    }
}
