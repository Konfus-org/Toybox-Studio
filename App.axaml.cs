using Toybox.Studio.Services;
using Toybox.Studio.Services.World;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Extensions;
using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Toybox.Studio.Shell;
using Toybox.Studio.Widgets.Ecs;
using Toybox.Studio.Widgets.LogConsole;
using Toybox.Studio.Widgets.Status;
using Toybox.Studio.Widgets.PropertyGrid;
using Toybox.Studio.Widgets.Toolbar;
using Toybox.Studio.Shell.Workspace;
using Toybox.Studio.Services.Clipboard;
using Toybox.Studio.Services.Commands;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.Favorites;
using Toybox.Studio.Services.Scripting;
using Toybox.Studio.Widgets.ContextMenu;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Services.Settings;
using Toybox.Studio.Services.Theming;
using Toybox.Studio.Widgets.Behaviors;
using Toybox.Studio.Widgets.Behaviors.Animations;

namespace Toybox.Studio;

public partial class App : Application
{
    // Minimum time the splash stays up so a fast startup doesn't flash; the brief pause between steps
    // lets each line of the splash log actually render.
    private static readonly TimeSpan SplashMinimumDuration = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan StepPause = TimeSpan.FromMilliseconds(140);

    private IHost? _host;

    // Set once the user has answered the unsaved-changes prompt so the programmatic re-close goes through.
    private bool _closeConfirmed;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Build the host before the splash so the splash can show the real log console and every
            // startup line lands in TbxStudio.log.
            var builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
            ConfigureServices(builder.Services);
            _host = builder.Build();
            _host.Start();

            // Resolving the logging service rotates and opens TbxStudio.log; the console subscribes to it.
            var log = _host.Services.GetRequiredService<Logger>();
            var console = _host.Services.GetRequiredService<LogConsoleViewModel>();

            var splashViewModel = new SplashViewModel(console);
            var splash = new SplashWindow { DataContext = splashViewModel };
            splash.Show();

            StartupAsync(desktop, splash, splashViewModel, log).FireAndForget();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Brings the app up step by step, narrating progress through the shared log (which the splash and
    /// main window both show). The main window is built hidden and only revealed once everything is
    /// ready and the splash has closed.
    /// </summary>
    private async Task StartupAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        SplashWindow splash,
        SplashViewModel splashViewModel,
        Logger log)
    {
        var timer = Stopwatch.StartNew();

        // Startup walks the UI: it builds windows, applies the theme, and toggles visibility, so every
        // await must resume back on the UI thread.
        async Task StepAsync(string message)
        {
            splashViewModel.Status = message;
            log.Info(message);
            await Task.Delay(StepPause).ContinueOnSameContext();
        }

        try
        {
            await StepAsync("Applying theme…").ContinueOnSameContext();
            var themeManager = _host!.Services.GetRequiredService<ThemeManager>();
            themeManager.ApplySavedTheme();
            // Themes load before the logger exists, so surface any malformed-file warnings now.
            foreach (var warning in themeManager.LoadWarnings)
                log.Warning(warning);

            // Keep the motion tokens in sync with the saved Animation-intensity setting: Listen fires immediately
            // (so the juicy transitions are live from the first frame, independent of the theme) and again on every
            // settings save, so a changed intensity re-publishes without the Settings panel pushing it by hand.
            var settingsManager = _host.Services.GetRequiredService<SettingsManager>();
            settingsManager.Listen(
                () => MotionTokens.Publish(settingsManager.Settings.Accessibility.AnimationIntensity));

            // Give the property grid's custom widgets (asset pickers, script links) the services they
            // need before any inspector or settings grid is built.
            var catalog = _host.Services.GetRequiredService<AssetCatalog>();
            PropertyViewRegistry.Configure(
                catalog,
                _host.Services.GetRequiredService<WorldManager>());

            // Eagerly build the component/script catalogs so they subscribe to the session (and the asset
            // catalog) and are populated by the time the inspector's "Add" pickers open, even if the
            // inspector dockable is opened after the first connect.
            _host.Services.GetRequiredService<ComponentCatalog>();
            _host.Services.GetRequiredService<ScriptCatalog>();

            // Give the inspector's script cards their inline editor / pop-out / source-resolution service.
            ScriptEditing.Current = _host.Services.GetRequiredService<ScriptEditing>();

            // Publish the data-driven context-menu service so the static MenuOpenBehavior (an attached
            // property) can resolve menus, the favorites store and the selection without a per-view hookup.
            ContextMenuService.Current = _host.Services.GetRequiredService<ContextMenuService>();

            // The Accessibility ▸ Animation intensity setting renders as a clay slider rather than a numeric
            // field (tagged [View("intensitySlider")] by the settings grid; see SettingsViewModel.TagView).
            PropertyViewRegistry.Register(
                "intensitySlider", (node, commit) => new SliderPropertyViewModel(node, commit));

            // Activating an asset link (e.g. a handle hyperlink) reveals the file in the OS explorer.
            catalog.AssetActivated += id =>
            {
                var project = _host!.Services.GetRequiredService<ProjectManager>().CurrentProject;
                if (catalog.Resolve(id) is { } asset && project is not null)
                    FileReveal.InExplorer(ResolveAssetPath(project, asset.Path));
            };

            await StepAsync("Building workspace…").ContinueOnSameContext();
            // The shell (and its console widget) must exist before discovery so startup log
            // lines land in the console.
            var shell = _host.Services.GetRequiredService<ShellViewModel>();
            var mainWindow = new MainWindow
            {
                DataContext = shell,
                IsVisible = false, // Revealed only after the splash closes.
            };
            desktop.MainWindow = mainWindow;
            // Closing the splash must not end the app; tie the lifetime to the main window instead.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.Exit += OnExit;

            // One consolidated unsaved-changes prompt on app close: veto the first close, ask once for every
            // dirty panel + the world, then close for real (or stay open on Cancel).
            mainWindow.Closing += async (_, args) =>
            {
                if (_closeConfirmed)
                    return;
                args.Cancel = true;
                if (await shell.RequestCloseAsync().ContinueOnSameContext())
                {
                    _closeConfirmed = true;
                    mainWindow.Close();
                }
            };

            await StepAsync("Locating engine…").ContinueOnSameContext();
            var session = _host.Services.GetRequiredService<Session>();
            // Resolve the watcher before any launch so it observes the engine from the very first signal.
            _host.Services.GetRequiredService<EngineWatcher>();
            // Bring the freeze watchdog up too, so an unresponsive engine is caught from the first launch and
            // the editor can offer to force-restart it instead of hanging on the frozen process.
            _host.Services.GetRequiredService<EngineWatchdog>();
            // Bring the inspector's live-refresh coordinator to life so it drives the play-mode pull (it holds
            // the shared inspector view-model and the play/selection subscriptions).
            _host.Services.GetRequiredService<InspectorRefreshCoordinator>();
            // Bring selection-sync to life so viewport selection changes are pushed to the engine for
            // highlighting (and re-pushed on every (re)connect).
            _host.Services.GetRequiredService<SelectionSync>();
            // Bring gizmo-sync to life so the transform-tool toolbar is pushed to the engine.
            _host.Services.GetRequiredService<GizmoSync>();
            // Bring the gizmo→toolbar bridge to life so the data-driven toolbar reflects the active gizmo.
            _host.Services.GetRequiredService<GizmoToolbarBridge>();
            // And the pause→toolbar bridge so the game transport's Pause/Resume toggle reflects pause state.
            _host.Services.GetRequiredService<PlayToolbarBridge>();
            var locator = _host.Services.GetRequiredService<Locator>();
            var locatorReport = locator.ResolveAtStartup();
            log.Info(locatorReport);
            splashViewModel.Status =
                locator.IsLocated ? "Engine located." : "Engine not found — set it in Settings.";

            await StepAsync("Watching for running engines…").ContinueOnSameContext();
            var detector = _host.Services.GetRequiredService<InstanceDetector>();
            detector.InstanceDetected += port => session.AttachAsync(port).FireAndForget();
            detector.Start();

            // Launch into a world at startup (the default). With no project open, fall back to the
            // bundled template world (skybox + grass ground + a cube that falls). The compile/launch
            // runs in the background so the editor opens immediately.
            var settings = _host.Services.GetRequiredService<SettingsManager>().Settings;
            var projects = _host.Services.GetRequiredService<ProjectManager>();
            var wantsLaunch = settings.Engine.AutoLaunchEngine
                || desktop.Args?.Contains("--auto-launch") == true;
            if (wantsLaunch && locator.IsLocated)
            {
                if (projects.CurrentProject is null)
                {
                    await StepAsync("Preparing the default world…").ContinueOnSameContext();
                    if (!projects.TryOpenDefaultTemplate(out var templateError))
                        log.Error(templateError ?? "Could not open the default template world.");
                }

                if (projects.CurrentProject is not null)
                {
                    log.Info("Launching engine…");
                    session.LaunchAsync(CancellationToken.None).FireAndForget();
                }
            }

            await StepAsync("Ready.").ContinueOnSameContext();

            var remaining = SplashMinimumDuration - timer.Elapsed;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining).ContinueOnSameContext();

            // Fully ready: close the splash first, then reveal the main window.
            splash.Close();
            mainWindow.IsVisible = true;
            mainWindow.Activate();

            if (!locator.IsLocated)
            {
                Popups.ShowMessageAsync(
                    "Engine Not Found",
                    "The Toybox Engine source folder could not be located automatically. "
                    + "Open Settings (⚙) and point the Engine path at your engine checkout.",
                    mainWindow).FireAndForget();
            }
        }
        catch (Exception exception)
        {
            // Surface the failure on the splash, then get out of the way so the app isn't stuck behind it.
            splashViewModel.Status = $"Startup failed: {exception.Message}";
            log.Error($"Startup failed: {exception.Message}");
            await Task.Delay(TimeSpan.FromSeconds(3)).ContinueOnSameContext();
            splash.Close();
            if (desktop.MainWindow is { } window)
                window.IsVisible = true;
        }
    }

    /// <summary>
    /// Resolves an asset's project-relative path to an absolute one for revealing. Tries the project
    /// root then the Assets folder (paths are reported relative to one of the two), and passes through
    /// an already-rooted path.
    /// </summary>
    private static string ResolveAssetPath(ProjectInfo project, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return project.AssetsDirectory;

        if (Path.IsPathRooted(relativePath))
            return relativePath;

        var underRoot = Path.Combine(project.RootDirectory, relativePath);
        if (File.Exists(underRoot) || Directory.Exists(underRoot))
            return underRoot;

        var underAssets = Path.Combine(project.AssetsDirectory, relativePath);
        if (File.Exists(underAssets) || Directory.Exists(underAssets))
            return underAssets;

        return underRoot;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<CommandRunner>();
        services.AddSingleton<SettingsManager>();
        services.AddSingleton<ThemeManager>();
        services.AddSingleton<LogFile>();
        services.AddSingleton<Logger>();
        services.AddSingleton<ProjectManager>();
        services.AddSingleton<FilePicker>();
        services.AddSingleton<CMakeCompiler>();
        services.AddSingleton<ProjectBuilder>();
        services.AddSingleton<EngineRpc>();
        services.AddSingleton<EngineSettings>();
        // Each viewport/game-view owns its own engine view stream (parameterized by ViewKind), so it's vended
        // by a factory rather than resolved directly — keeping EngineRpc (the transport) out of the view-models.
        services.AddSingleton<Func<ViewKind, ViewportStream>>(sp =>
            kind => new ViewportStream(
                sp.GetRequiredService<Session>(), sp.GetRequiredService<EngineRpc>(), kind));
        services.AddSingleton<JsonParser>();
        services.AddSingleton<WorldSelection>();
        services.AddSingleton<SelectionSync>();
        services.AddSingleton<GizmoTool>();
        services.AddSingleton<GizmoSync>();
        services.AddSingleton<ToolbarState>();
        services.AddSingleton<GizmoToolbarBridge>();
        services.AddSingleton<PlayToolbarBridge>();
        services.AddSingleton<Clipboard>();
        services.AddSingleton<EditorCommands>();
        services.AddSingleton<ToolCommandRunner>();
        services.AddSingleton<FavoritesManager>();
        services.AddSingleton<ContextMenuService>();
        services.AddSingleton<Session>();
        services.AddSingleton<EngineWatcher>();
        services.AddSingleton<EngineWatchdog>();
        services.AddSingleton<InstanceDetector>();
        services.AddSingleton<WorldManager>();
        services.AddSingleton<AssetCatalog>();
        services.AddSingleton<ComponentCatalog>();
        services.AddSingleton<ScriptCatalog>();
        services.AddSingleton<Locator>();
        services.AddSingleton<ThemeCreator>();
        services.AddSingleton<StatusViewModel>();
        services.AddSingleton<InspectorRefreshCoordinator>();

        // Script editor: the loopback server that hosts the vendored Monaco bundle and the shared document
        // buffers both editor surfaces (inline strip + popped-out window) bind to. The server is IDisposable
        // and disposed with the host on shutdown.
        services.AddSingleton<MonacoAssetServer>();
        services.AddSingleton<ScriptDocumentService>();
        services.AddSingleton<ScriptEditorLauncher>();
        services.AddSingleton<ScriptHotReload>();
        services.AddSingleton<ScriptEditing>();

        // Auto-registers every [Dockable] View's view-model (World, Viewport, Console, Settings, …) and the
        // catalog itself, so panels are declared only on their Views — no per-panel registration here.
        DockableCatalog.RegisterDockables(services);
        services.AddSingleton<LayoutStore>();
        services.AddSingleton<WorkspaceViewModel>();

        // Opens a new Asset Viewer panel for a chosen asset (File ▸ Open ▸ Asset), passing the target
        // asset to the freshly-spawned panel.
        services.AddSingleton<AssetViewerLauncher>();

        services.AddSingleton<ShellViewModel>();
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (_host is null)
            return;

        // Persist the working dock layout (docked + floating) so it's restored next launch. Runs on the UI
        // thread, before teardown, while the DockControl is still alive.
        try
        {
            _host.Services.GetRequiredService<WorkspaceViewModel>().SaveLastLayout();
        }
        catch (Exception)
        {
            // Never let a layout-save failure block shutdown.
        }

        // Run the async teardown on the thread pool: blocking the UI thread on code that
        // resumes via its SynchronizationContext would deadlock.
        var host = _host;
        _host = null;
        Task.Run(async () =>
        {
            await host.Services.GetRequiredService<Session>().DisposeAsync().ContinueOnAnyContext();
            await host.StopAsync(TimeSpan.FromSeconds(5)).ContinueOnAnyContext();
        }).Wait(TimeSpan.FromSeconds(15));
        host.Dispose();
    }
}
