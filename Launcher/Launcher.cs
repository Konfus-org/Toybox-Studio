using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Dock.Settings;
using Microsoft.Extensions.DependencyInjection;
using Toybox.Studio.AppHosting;
using Toybox.Studio.Assets;
using Toybox.Studio.Behaviors.Animations;
using Toybox.Studio.CMake;
using Toybox.Studio.Dialogs;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Gizmos;
using Toybox.Studio.Logging;
using Toybox.Studio.MenuBar;
using Toybox.Studio.Projects;
using Toybox.Studio.Settings;
using Toybox.Studio.SettingsEditor;
using Toybox.Studio.Shell;
using Toybox.Studio.Status;
using Toybox.Studio.Themes;
using Toybox.Studio.Utils;
using Toybox.Studio.Viewport;
using Toybox.Studio.Workspaces;

namespace Toybox.Studio;

/// <summary>
/// The app's bootstrap and composition root. <see cref="LaunchAsync"/> is the one endpoint: it boots
/// Avalonia, configures the service provider (every service registered against exactly what it asks
/// for in its constructor — nothing ever takes the provider itself), and runs the startup flow: the
/// project picker, then the splash narrating the picked project's compile + engine launch, then the
/// studio window, which takes over the app's lifetime.
/// </summary>
public sealed class Launcher
{
    // Minimum time the splash stays up so a fast launch doesn't flash.
    private static readonly TimeSpan SplashMinimumDuration = TimeSpan.FromMilliseconds(900);

    // How long the handoff waits for the splash icon's Ready nod (its declared clip duration plus a
    // beat) before starting the fade, so the bow isn't cut off mid-dip. Tracks the NodClip declared
    // on the Ready state in SplashWindow.axaml (0.7s).
    private static readonly TimeSpan ReadyNodDuration = TimeSpan.FromMilliseconds(950);

    // How long app exit waits for the hosted engine's graceful stop before it is killed.
    private static readonly TimeSpan EngineTeardownTimeout = TimeSpan.FromSeconds(15);

    // How long app exit waits for the Closing-time layout save's file write before giving up on it.
    private static readonly TimeSpan LayoutSaveTimeout = TimeSpan.FromSeconds(3);

    private readonly SettingsManager _settings;
    private readonly Project _project;
    private readonly ProjectLoader _projectLoader;
    private readonly ProjectFactory _projectFactory;
    private readonly EngineCoordinator _coordinator;
    private readonly Logger _log;
    private readonly MainWindowViewModel _mainViewModel;
    private readonly SplashViewModel _splashViewModel;
    private readonly SplashWindow _splash;

    // The Closing-time layout save, awaited by Shutdown so the process doesn't exit under its write.
    private Task _layoutSave = Task.CompletedTask;

    public Launcher(
        SettingsManager settings,
        Project project,
        ProjectLoader projectLoader,
        ProjectFactory projectFactory,
        EngineCoordinator coordinator,
        Logger log,
        SplashViewModel splashViewModel,
        MainWindowViewModel mainViewModel)
    {
        _settings = settings;
        _project = project;
        _projectLoader = projectLoader;
        _projectFactory = projectFactory;
        _coordinator = coordinator;
        _log = log;
        _mainViewModel = mainViewModel;
        _splashViewModel = splashViewModel;
        _splash = new SplashWindow { DataContext = splashViewModel };
    }

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            LaunchAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            // The outermost net, for crashes before CrashGuard's hooks exist (or escaping them): record,
            // then rethrow so the process still fails loudly (exit code, Windows error reporting).
            CrashGuard.ReportFatal(exception);
            throw;
        }
    }

    /// <summary>
    /// Boots the whole studio: builds the Avalonia app, configures the service provider, and runs the
    /// startup flow inside the UI loop. The returned task completes when the app exits.
    /// </summary>
    public static Task LaunchAsync(string[] args)
    {
        using var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = args,
            // Nothing owns the app's lifetime until the studio window exists, so shutdown stays
            // explicit for now — closing the splash or picker mid-launch must not end the app.
            // RunAsync hands the lifetime to the studio window when it opens.
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        BuildAvaloniaApp().SetupWithLifetime(lifetime);

        var services = ConfigureServices();
        Boot(services);
        lifetime.Exit += (_, _) => Shutdown(services);

        // The flow is interactive (windows, dialogs), so it is posted to run once Start() enters the loop.
        var launcher = services.GetRequiredService<Launcher>();
        Dispatcher.UIThread.Post(() => launcher.RunAsync(lifetime).FireAndForget());
        lifetime.Start();
        return Task.CompletedTask;
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    /// <summary>
    /// The composition root: every service the studio is composed of, registered as a singleton taking
    /// exactly what it uses in its constructor. Plain values (settings knobs, ports) are supplied by
    /// the factory lambdas here — no class ever sees the provider.
    /// </summary>
    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Settings — nearly everything below reads them — and the theme derived from them.
        services.AddSingleton<SettingsManager>();
        services.AddSingleton<ThemeManager>();

        // Unified logging: TbxStudio.log, with the engine console's colours tracking the active theme
        // via the ThemeLogSource adapter.
        services.AddSingleton(sp =>
            new Logger(new LogFile(), new ThemeLogSource(sp.GetRequiredService<ThemeManager>())));

        // The event bus every domain signal flows through — publishers dispatch typed structs,
        // handlers register; nobody holds anybody.
        services.AddSingleton<EventDispatcher>();

        // The active project — pure data, populated by the launch flow through the loader once the
        // user picks one — and the build/ship services callers compose around it. Both builders share
        // the one locator and build runner, so the engine path and build settings are read live from
        // the settings, and at most one native build runs across the two.
        services.AddSingleton<Project>();
        services.AddSingleton<ProjectLoader>();
        services.AddSingleton<ProjectFactory>();
        services.AddSingleton<CommandRunner>();
        services.AddSingleton<CMakeCompiler>();
        services.AddSingleton<EngineSourceLocator>();
        services.AddSingleton<BuildRunner>();
        services.AddSingleton<ProjectBuilder>();
        services.AddSingleton<EngineBuilder>();
        services.AddSingleton<ProjectShipper>();

        // The engine and its host: the engine service is what everything talks to (and reads State
        // from); the host itself only ever launches/attaches/stops what it is handed. The coordinator
        // builds the active project and launches its engine when the flow asks.
        services.AddSingleton<Engine>();
        services.AddSingleton(sp => new AppHost<Engine>(
            sp.GetRequiredService<Engine>(),
            sp.GetRequiredService<Logger>(),
            sp.GetRequiredService<EventDispatcher>(),
            sp.GetRequiredService<SettingsManager>().Editor.Engine.RestartOnCrash));

        // The engine-sync connection point: every engine-mirrored object (entities, components,
        // assets) binds here to push edits and receive the engine's sync.changed deltas.
        services.AddSingleton<SyncHub>();

        // The editor's gizmo overlay: named retained drawing layers the engine renders over editor
        // viewports; the hub owns them and re-pushes on every (re)connect.
        services.AddSingleton<Gizmo>();

        // The asset domain: the catalog mirrors the engine's registered assets, refreshing itself as
        // the connection comes up. The asset lifecycle lives on the assets themselves (constructing
        // with an id loads; saving creates), wired to its services once via Asset.Configure in Boot.
        services.AddSingleton<AssetCatalog>();

        // Generic owned-app supervision: ping the connected engine so a freeze is noticed, and while
        // disconnected watch for an engine that is already running (e.g. launched by a debugger) to
        // attach to instead of launching a second one. The coordinator decides what to do with what
        // it reports.
        services.AddSingleton(sp => new OwnedAppWatchdog(
            sp.GetRequiredService<Engine>(),
            EngineCommands.EnginePing,
            Engine.DefaultPort,
            sp.GetRequiredService<EventDispatcher>()));
        services.AddSingleton(sp =>
        {
            var engineSettings = sp.GetRequiredService<SettingsManager>().Editor.Engine;
            return new EngineCoordinator(
                sp.GetRequiredService<Project>(),
                sp.GetRequiredService<ProjectBuilder>(),
                sp.GetRequiredService<AppHost<Engine>>(),
                sp.GetRequiredService<OwnedAppWatchdog>(),
                engineSettings.HideEngineWindow,
                engineSettings.ConnectTimeoutSeconds,
                sp.GetRequiredService<Logger>(),
                sp.GetRequiredService<EventDispatcher>());
        });

        // The docking workspace: the catalog scans the feature assemblies for [Dockable] Views, and the
        // composition root authors each panel's view-model factory here — view-models are never
        // service-registered; a panel's lifetime belongs to the workspace. The viewport factory builds
        // a fresh view-model per opened panel (each spawns its own editor-camera stream into the
        // engine's world, disposed when the panel closes); Settings closes over one lazily-created
        // instance, so its panel survives close/reopen. Popups is the app-wide modal service.
        services.AddSingleton(sp =>
        {
            SettingsViewModel? settings = null;
            return new DockableFactories()
                .Add(() =>
                {
                    var viewport = new ViewportViewModel(
                        sp.GetRequiredService<EventDispatcher>(), sp.GetRequiredService<Logger>(), ViewKind.Editor);
                    viewport.Prepare(new ViewportStream(
                        sp.GetRequiredService<Engine>(), sp.GetRequiredService<EventDispatcher>(), ViewKind.Editor));
                    return viewport;
                })
                .Add(() => settings ??= new SettingsViewModel(sp.GetRequiredService<SettingsManager>()));
        });
        services.AddSingleton(sp => new DockableCatalog(
            sp.GetRequiredService<DockableFactories>(),
            sp.GetRequiredService<Logger>(),
            typeof(ViewportView).Assembly, typeof(SettingsView).Assembly));
        services.AddSingleton<Popups>();

        // The launch flow, carrying the window view-model graph it drives. View-models are composed
        // here — parents construct (or receive) their children; none is a service, so no panel or
        // window state is injectable. (The project picker's view-model is the one construct built
        // later, in the flow: it needs the picker window's own storage provider for its Browse dialog.)
        services.AddSingleton(sp =>
        {
            var workspace = new WorkspaceViewModel(
                sp.GetRequiredService<DockableCatalog>(),
                sp.GetRequiredService<Popups>(),
                sp.GetRequiredService<Logger>());
            var menuBar = new MenuBarViewModel(
                sp.GetRequiredService<Project>(),
                sp.GetRequiredService<ProjectBuilder>(),
                sp.GetRequiredService<EngineBuilder>(),
                sp.GetRequiredService<AppHost<Engine>>(),
                workspace,
                sp.GetRequiredService<Logger>(),
                sp.GetRequiredService<EventDispatcher>());
            var status = new StatusViewModel(sp.GetRequiredService<EventDispatcher>());
            return new Launcher(
                sp.GetRequiredService<SettingsManager>(),
                sp.GetRequiredService<Project>(),
                sp.GetRequiredService<ProjectLoader>(),
                sp.GetRequiredService<ProjectFactory>(),
                sp.GetRequiredService<EngineCoordinator>(),
                sp.GetRequiredService<Logger>(),
                new SplashViewModel(sp.GetRequiredService<EventDispatcher>()),
                new MainWindowViewModel(menuBar, status, workspace));
        });

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>
    /// Brings up the order-sensitive services the lazy provider would otherwise leave dormant: the theme
    /// is applied before any window renders, its load warnings flush once the logger exists, every log
    /// line mirrors into the console view, the sync hub starts listening, and the watchdog starts probing.
    /// </summary>
    private static void Boot(ServiceProvider services)
    {
        // Theme loading runs before the logger exists, so its warnings are flushed once the logger does.
        var theme = services.GetRequiredService<ThemeManager>();
        theme.ApplySavedTheme();

        // Publish the motion tokens before any window exists: they gate EVERY animation (the splash's
        // rock/spin/nod and the micro-animation behaviors all read the AnimationIntensity resource, and
        // an unpublished token reads as 0 — motion off). The Settings window re-publishes live as the
        // intensity value is edited.
        MotionTokens.Publish(services.GetRequiredService<SettingsManager>().Editor.Accessibility.AnimationIntensity);

        var log = services.GetRequiredService<Logger>();

        // Dock's own docking diagnostics flow into the studio log: a drag logs which drop control was
        // considered and why adorners were (or weren't) shown, so a failing drag-and-drop names the
        // check that rejected it instead of failing silently.
        DockSettings.EnableDiagnosticsLogging = true;
        DockSettings.DiagnosticsLogHandler = message => log.Info(message);

        // From here on no crash is silent: UI exceptions log and are survived, fatal ones leave a
        // synchronous crash file beside the logs.
        CrashGuard.Install(log);

        foreach (var warning in theme.LoadWarnings)
            log.Warning(warning);

        // Purely event-driven services nobody injects — resolved so they exist and subscribe.
        services.GetRequiredService<SyncHub>();
        services.GetRequiredService<Gizmo>();
        services.GetRequiredService<AssetCatalog>();
        services.GetRequiredService<OwnedAppWatchdog>().Start();

        // Assets are constructed, not injected, so their lifecycle services are wired statically once.
        Asset.Configure(
            services.GetRequiredService<SyncHub>(), services.GetRequiredService<AssetCatalog>(), log);
    }

    /// <summary>
    /// Stops the hosted engine (gracefully, killing on timeout) as the app exits. Teardown order is
    /// engine-critical, so it stays explicit rather than leaning on the provider's reverse-creation-order
    /// disposal (which also couldn't await the host); the process exits right after, so the provider
    /// itself is left undisposed.
    /// </summary>
    private static void Shutdown(ServiceProvider services)
    {
        var launcher = services.GetRequiredService<Launcher>();

        // The workspace's open panel view-models go first: each viewport's view.stop needs the
        // engine connection the teardown below closes. (The layout itself was already captured at
        // MainWindow.Closing — by now the floating windows have closed and left the dock model —
        // but its file write may still be in flight; see below.)
        launcher.DisposeWorkspacePanels();

        // Let the Closing-time layout save's write land before the process exits. Safe to block on:
        // the layout was serialized synchronously in the Closing handler, and the store's remaining
        // continuation is context-free (thread pool), so it can't need this (UI) thread.
        launcher.CompleteLayoutSave();

        services.GetRequiredService<EngineCoordinator>().Dispose();
        services.GetRequiredService<OwnedAppWatchdog>().Dispose();
        services.GetRequiredService<AssetCatalog>().Dispose();
        services.GetRequiredService<Gizmo>().Dispose();
        services.GetRequiredService<SyncHub>().Dispose();
        // Run the async teardown on the thread pool: blocking the UI thread on code that resumes
        // via its SynchronizationContext would deadlock.
        Task.Run(async () =>
                await services.GetRequiredService<AppHost<Engine>>().DisposeAsync().ContinueOnAnyContext())
            .Wait(EngineTeardownTimeout);
    }

    /// <summary>
    /// The interactive startup flow, run inside the UI loop: prompt for the project first, and only
    /// once one is picked (or created) show the splash narrating its compile + engine launch (failures
    /// land in the log and on the splash), then swap the splash out for the studio window. Just closing
    /// the picker quits the app — nobody asked for a studio. Touches windows, so every await resumes
    /// back on the UI thread.
    /// </summary>
    private async Task RunAsync(ClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            if (await PromptForProjectAsync().ContinueOnSameContext() == StartupChoice.Quit)
            {
                _log.Info("The project picker was closed without a choice; exiting.");
                _splash.Close();
                desktop.Shutdown();
                return;
            }

            // A project was picked (or just created) — it gives the splash something to narrate, so it
            // appears here, with the project's own icon already on it.
            _splash.Show();
            var timer = Stopwatch.StartNew();

            if (_settings.Editor.Engine.AutoLaunchEngine)
            {
                await _coordinator.StartEngineAsync().ContinueOnSameContext();
            }
            else
            {
                _log.Info("Engine auto-launch is disabled in the editor settings; not launching.");
                _splashViewModel.Status = "Engine auto-launch is off.";
            }

            var remaining = SplashMinimumDuration - timer.Elapsed;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining).ContinueOnSameContext();
        }
        catch (Exception exception)
        {
            _log.Error($"Launch failed: {exception.Message}");
            // The failure may predate the splash (a picker mishap); make sure it's on screen to carry
            // the message.
            if (!_splash.IsVisible)
                _splash.Show();
            _splashViewModel.Status = $"Launch failed: {exception.Message}";
            await Task.Delay(TimeSpan.FromSeconds(3)).ContinueOnSameContext();
        }

        // Launch is done (or failed past the splash): only now does the studio window come to exist.
        // Show it behind the (topmost) splash, hand it the app's lifetime, then fade the splash down
        // into it.
        var mainWindow = new MainWindow { DataContext = _mainViewModel };
        // The dock layout must be captured BEFORE the window starts closing: floating panels are OS
        // windows owned by this one, and closing them (part of this window's close) removes them from
        // the dock model — a save in Shutdown() would persist a layout with every float stripped.
        // The save snapshots the layout synchronously here; the kept task is its in-flight file
        // write, which Shutdown() waits out before the process exits.
        mainWindow.Closing += (_, _) => _layoutSave = _mainViewModel.SaveLayoutAsync();
        desktop.MainWindow = mainWindow;
        mainWindow.Show();
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        await CloseSplashAsync().ContinueOnSameContext();
        mainWindow.Activate();
    }

    /// <summary>
    /// Hands the screen to the studio window: flips the splash to Ready so its icon takes the bow (the
    /// nod — the engine's own Ready state usually lands only after this handoff, so the flow declares
    /// it), lets it land while the studio window paints its first frame behind the topmost splash, then
    /// triggers the fade-down dismissal (the splash window closes itself when the fade ends — instantly
    /// at animation intensity 0). The timeout backstop means a splash that can't animate never strands
    /// topmost over the editor.
    /// </summary>
    private async Task CloseSplashAsync()
    {
        _splashViewModel.FinishLoading();
        await Task.Delay(ReadyNodDuration).ContinueOnSameContext();

        var closed = new TaskCompletionSource();
        _splash.Closed += (_, _) => closed.TrySetResult();
        _splashViewModel.Dismiss();
        await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(2))).ContinueOnSameContext();
        _splash.Close();
    }

    /// <summary>
    /// Asks the user which project to open with the picker: the known projects as a flat list, plus
    /// the Add row (a new project from the bundled template, or an existing one from disk). A valid
    /// pick is remembered in the editor settings and loaded into the active project; just closing the
    /// picker means quit.
    /// </summary>
    private async Task<StartupChoice> PromptForProjectAsync()
    {
        var picker = new ProjectPickerWindow();
        var viewModel = new ProjectPickerViewModel(
            _settings, _projectLoader, _projectFactory, picker.StorageProvider);
        picker.DataContext = viewModel;
        // Closing the window without choosing (the title-bar X) is a dismissal, not a choice.
        picker.Closed += (_, _) => viewModel.Dismiss();
        picker.Show();

        var root = await viewModel.Choice.ContinueOnSameContext();
        picker.Close();
        if (root is null)
            return StartupChoice.Quit;

        RememberProject(root);
        _projectLoader.Load(root, _project);
        _splashViewModel.ShowProject(_project);
        return StartupChoice.Project;
    }

    /// <summary>How the project picker concluded — what the startup flow does next hangs on it.</summary>
    private enum StartupChoice
    {
        /// <summary>A project was picked (or created) and loaded; launch it under the splash.</summary>
        Project,

        /// <summary>The picker was closed without a choice: quit the app.</summary>
        Quit,
    }

    /// <summary>Records the opened project as the last-opened and front of the recents (capped) list.</summary>
    private void RememberProject(string root)
    {
        const int maxRecent = 10;

        var projects = _settings.Editor.Projects;
        projects.LastOpened = root;
        projects.Recent.RemoveAll(p => string.Equals(p, root, StringComparison.OrdinalIgnoreCase));
        projects.Recent.Insert(0, root);
        if (projects.Recent.Count > maxRecent)
            projects.Recent.RemoveRange(maxRecent, projects.Recent.Count - maxRecent);

        _settings.SaveAsync().FireAndForget();
    }

    /// <summary>Disposes every open panel view-model (each viewport's engine view stops). Run first in
    /// shutdown, while the engine connection the disposals talk to is still up.</summary>
    internal void DisposeWorkspacePanels() => _mainViewModel.Workspace.DisposeInstances();

    /// <summary>Waits (bounded) for the Closing-time layout save's file write. The save never faults —
    /// the store logs and swallows its own I/O errors — so this only ever times out.</summary>
    internal void CompleteLayoutSave() => _layoutSave.Wait(LayoutSaveTimeout);
}
