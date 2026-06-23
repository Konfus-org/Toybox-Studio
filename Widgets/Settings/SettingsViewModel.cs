using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Services.Settings;
using Toybox.Studio.Services.Theming;
using Toybox.Studio.Shell.Panels;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Widgets.Settings;

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
    private readonly EngineSettings _engineSettings;

    public SettingsViewModel(
        SettingsManager settings,
        ThemeManager theme,
        ThemeCreator themeCreator,
        FilePicker filePicker,
        ProjectManager projects,
        Session session,
        Locator locator,
        EngineSettings engineSettings,
        Logger log,
        JsonParser parser)
    {
        _session = session;
        _engineSettings = engineSettings;

        Editor = new EditorSettingsViewModel(settings, theme, themeCreator, filePicker, locator, parser);
        Project = new ProjectSettingsViewModel(projects, engineSettings, parser, log);

        Editor.DirtyChanged += RecomputeDirty;
        Project.DirtyChanged += RecomputeDirty;

        // The project's AppSettings.json is lean (only its overrides); the full settings schema — graphics,
        // physics, etc., plus the plugins list's element_template — comes from the engine. Fetch it once the
        // engine connects and rebuild the project grid enriched. Until then the flat file renders as a fallback.
        session.StateChanged += OnSessionStateChanged;

        // The panel may be opened while the engine is already connected (StateChanged only fires on a
        // transition), so fetch the schema now too if so.
        if (_engineSettings.IsConnected)
            Project.LoadSchemaThenRefreshAsync().FireAndForget();

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

    private void OnSessionStateChanged(ConnectionState state)
    {
        if (state == ConnectionState.Connected)
            Project.LoadSchemaThenRefreshAsync().FireAndForget();
    }

    private void RecomputeDirty() => IsDirty = Editor.IsDirty || Project.IsDirty;
}
