using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Dialogs;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Logging;
using Toybox.Studio.Project;
using Toybox.Studio.Settings;
using Toybox.Studio.Theming;
using Toybox.Studio.Shell.Panels;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Settings;

/// <summary>
/// The Settings panel: a buffered <see cref="DataPanel"/> that owns the docked Save/Cancel footer and the
/// aggregate dirty state, and composes the two settings tabs — <see cref="EditorSettingsViewModel"/> (the
/// editor's own POCO + theme) and <see cref="ProjectSettingsViewModel"/> (the open project's AppSettings). Each
/// child holds its own working copy and reports dirtiness up; this coordinator commits both on Save and reverts
/// both on Cancel. Dirty state (any child differs from its last-saved baseline) shows as a '*' on the tab.
/// </summary>
public sealed partial class SettingsViewModel : DataPanel
{
    private readonly Session _session;

    public SettingsViewModel(
        SettingsManager settings,
        ThemeManager theme,
        ThemeCreator themeCreator,
        FilePicker filePicker,
        EngineLocator locator,
        Session session,
        Logger log)
    {
        _session = session;

        Editor = new EditorSettingsViewModel(settings, theme, themeCreator, filePicker, locator);
        // The project tab works entirely through the SettingsManager's project settings asset, which the manager
        // (re)loads — enriched with the engine schema — on project change / engine connect.
        Project = new ProjectSettingsViewModel(settings, log);

        Editor.DirtyChanged += RecomputeDirty;
        Project.DirtyChanged += RecomputeDirty;

        RecomputeDirty();
    }

    public override string BaseTitle => "Settings";

    /// <summary>The Editor tab: the editor's own settings POCO and the theme list.</summary>
    public EditorSettingsViewModel Editor { get; }

    /// <summary>The Project tab: the open project's AppSettings.</summary>
    public ProjectSettingsViewModel Project { get; }

    /// <summary>Settings search; filters both the Editor and Project grids by header or value.</summary>
    [ObservableProperty]
    public partial string Search { get; set; } = "";

    /// <summary>
    /// Commits both tabs (editor POCO + theme via C#, project settings via the engine), then — only if a
    /// confirmed build-affecting editor change (the C++ compiler or the engine source path) requires it —
    /// rebuilds and relaunches the engine. Invoked by the base <see cref="DataPanel.SaveAsync"/> (the footer's
    /// Save).
    /// </summary>
    protected override async Task CommitAsync()
    {
        var recompile = await Editor.CommitAsync().ContinueOnSameContext();
        await Project.CommitAsync(CancellationToken.None).ContinueOnSameContext();
        RecomputeDirty();

        if (recompile)
            await _session.RebuildAndRelaunchAsync().ContinueOnSameContext();
    }

    /// <summary>Discards every unsaved edit in both tabs. Invoked by the base <see cref="DataPanel.Cancel"/>.</summary>
    protected override void RevertChanges()
    {
        Editor.Revert();
        Project.Revert();
    }

    private void RecomputeDirty() => IsDirty = Editor.IsDirty || Project.IsDirty;
}
