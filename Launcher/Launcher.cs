using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Dock.Settings;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Toybox.Studio.AssetBrowser;
using Toybox.Studio.AssetViewer;
using Toybox.Studio.Behaviors;
using Toybox.Studio.Behaviors.Animations;
using Toybox.Studio.Clipboards;
using Toybox.Studio.CMake;
using Toybox.Studio.Coding;
using Toybox.Studio.ContextMenu;
using Toybox.Studio.Dialogs;
using Toybox.Studio.Docking;
using Toybox.Studio.EngineApi;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types.Gizmos;
using Toybox.Studio.EngineApi.Types.Physics;
using Toybox.Studio.EngineApi.Types.Worlds;
using Toybox.Studio.Events;
using Toybox.Studio.Favorites;
using Toybox.Studio.GameViewer;
using Toybox.Studio.Git;
using Toybox.Studio.Hosting;
using Toybox.Studio.Keybindings;
using Toybox.Studio.LogConsole;
using Toybox.Studio.Logging;
using Toybox.Studio.MenuBar;
using Toybox.Studio.Monaco;
using Toybox.Studio.Projects;
using Toybox.Studio.Settings;
using Toybox.Studio.Status;
using Toybox.Studio.Themes;
using Toybox.Studio.Toolbar;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Composition;
using Toybox.Studio.Viewport;
using Toybox.Studio.NodeGraph;
using Toybox.Studio.WorldTree;
using Toybox.Studio.WorldViewer;

namespace Toybox.Studio;

/// <summary>
/// The app's bootstrap and composition root. <see cref="LaunchAsync"/> is the one endpoint: it boots
/// Avalonia, configures the service provider (every service registered against exactly what it asks
/// for in its constructor — the <see cref="ViewModelFactory"/> is the one type that holds the provider,
/// and it exists so view-models don't have to be hand-wired here), and runs the startup flow: the
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
    private readonly ProjectPaths _projectPaths;
    private readonly ProjectLoader _projectLoader;
    private readonly ProjectFactory _projectFactory;
    private readonly EngineCoordinator _coordinator;
    private readonly EngineSourceLocator _engineLocator;
    private readonly GitClient _git;
    private readonly Popups _popups;
    private readonly EditorKeymap _keymap;
    private readonly EventDispatcher _events;
    private readonly Logger _log;
    private readonly ViewModelFactory _viewModels;
    private readonly MainWindowViewModel _mainViewModel;
    private readonly SplashViewModel _splashViewModel;
    private readonly SplashWindow _splash;

    // The Closing-time layout save, awaited by Shutdown so the process doesn't exit under its write.
    private Task _layoutSave = Task.CompletedTask;

    public Launcher(
        SettingsManager settings,
        Project project,
        ProjectPaths projectPaths,
        ProjectLoader projectLoader,
        ProjectFactory projectFactory,
        EngineCoordinator coordinator,
        EngineSourceLocator engineLocator,
        GitClient git,
        Popups popups,
        EditorKeymap keymap,
        EventDispatcher events,
        Logger log,
        ViewModelFactory viewModels)
    {
        _settings = settings;
        _project = project;
        _projectPaths = projectPaths;
        _projectLoader = projectLoader;
        _projectFactory = projectFactory;
        _coordinator = coordinator;
        _engineLocator = engineLocator;
        _git = git;
        _popups = popups;
        _keymap = keymap;
        _events = events;
        _log = log;
        _viewModels = viewModels;
        _splashViewModel = viewModels.Create<SplashViewModel>();
        _mainViewModel = viewModels.Create<MainWindowViewModel>();
        _splash = new SplashWindow { DataContext = _splashViewModel };
    }

    [STAThread]
    public static void Main(string[] args)
    {
        try
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
        }
        catch (Exception exception)
        {
            // The outermost net, for crashes before CrashGuard's hooks exist (or escaping them): record,
            // then rethrow so the process still fails loudly (exit code, Windows error reporting).
            CrashGuard.ReportFatal(exception);
            throw;
        }
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
    /// the factory lambdas here. View-models aren't registered — the <see cref="ViewModelFactory"/> (the
    /// one type given the provider) builds them on demand, resolving their services automatically.
    /// </summary>
    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // The registry of every well-known filesystem path (~/.toybox data + bundled templates);
        // injected wherever a path is needed, so nothing hard-codes a folder or file name.
        services.AddSingleton<PathsCatalog>();

        // Settings — nearly everything below reads them — and the theme derived from them.
        services.AddSingleton<SettingsManager>();
        services.AddSingleton<ThemeManager>();

        // Unified logging: TbxStudio.log, with the engine console's colours tracking the active theme
        // via the ThemeLogSource adapter.
        services.AddSingleton(sp =>
            new Logger(
                new LogFile(sp.GetRequiredService<PathsCatalog>()),
                new ThemeLogSource(sp.GetRequiredService<ThemeManager>())));

        // The event bus every domain signal flows through — publishers dispatch typed structs,
        // handlers register; nobody holds anybody.
        services.AddSingleton<EventDispatcher>();

        // The rebindable action layer: the registry (features register their actions at
        // construction), the editor keymap (their bindings, a real .inputmap in ~/.toybox, reloaded
        // by the launch flow once registrations exist), and the dispatcher the main window's
        // key-downs run through.
        services.AddSingleton<ActionRegistry>();
        services.AddSingleton<EditorKeymap>();
        services.AddSingleton<KeybindingDispatcher>();

        // The user's starred menu items: the disk store (one file per surface under ~/.toybox/Favorites)
        // and the manager the menu bar and context menus both toggle through, dispatching FavoritesChanged.
        services.AddSingleton<FavoritesStore>();
        services.AddSingleton<FavoritesManager>();

        // The JSON-over-OS clipboard shared by the context menus (copy/paste of values and asset handles).
        services.AddSingleton<Clipboard>();

        // The type-routed context menus: the catalog discovers every ContextMenu<T> in the ContextMenu
        // assembly and the adapter shows the built menu in a flyout. Both are published to their statics in
        // Boot so the low ContextMenuOpener attach-behavior (opted in per view) can reach them. The asset
        // menu's CRUD verbs act through AssetOperations (file moves under the project root + engine coordination).
        services.AddSingleton<AssetOperations>();
        ContextMenuCatalog.Register(services);
        services.AddSingleton<ContextMenuOpenerAdapter>();

        // The active project — pure data, populated by the launch flow through the loader once the
        // user picks one — and the build/ship services callers compose around it. Both builders share
        // the one locator and build runner, so the engine path and build settings are read live from
        // the settings, and at most one native build runs across the two.
        services.AddSingleton<Project>();
        services.AddSingleton<ProjectPaths>();
        services.AddSingleton<ProjectLoader>();
        services.AddSingleton<ProjectFactory>();
        services.AddSingleton<CommandRunner>();
        services.AddSingleton<CMakeCompiler>();
        services.AddSingleton<GitClient>();
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

        // The runtime physics queries (raycasts): engine-global state with no address, bound to the
        // hub for its outbound commands only.
        services.AddSingleton(sp =>
        {
            var physics = new Physics();
            physics.Bind(sp.GetRequiredService<SyncHub>());
            return physics;
        });

        // The editor's entity selection: engine-global synced state every panel observes; it re-pushes
        // itself to the engine on every (re)connect, so it registers with the event bus and binds here.
        services.AddSingleton(sp =>
        {
            var selection = new WorldSelection(
                sp.GetRequiredService<EventDispatcher>(), sp.GetRequiredService<Logger>());
            selection.Bind(sp.GetRequiredService<SyncHub>());
            return selection;
        });

        // The world viewport's transform tool: the active gizmo mode + snapping the viewport toolbars
        // and the Q/W/E/R keybindings drive; a pure handler that pushes the active handle set to the
        // engine on every change and (re)connect (its actions are registered by WorldViewerToolbarActions).
        services.AddSingleton<WorldViewerTool>();

        // The world viewport's render layers: the collider wireframe modes, post-processing toggle, and
        // render-stage debug view the render-layers toolbar drives; a pure handler that pushes the state to
        // the engine on every change and (re)connect (actions registered by WorldViewerToolbarActions).
        services.AddSingleton<RenderLayers>();

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

        // The world viewport's click-select (lands taps on the shared selection). The World Viewer panel
        // itself mints each fully-wired pane (viewport + its overlay toolbars + pick handler + engine
        // stream) from these editor collaborators, so there is no separate pane-factory service.
        services.AddSingleton<WorldPickHandler>();

        // The world viewport's action registrations — the gizmo tools (Q/W/E/R + snap) and the render-layer
        // toggles, scoped to the world viewport panel (Scheme.For<WorldViewerViewModel>()). Registered
        // here rather than by the executing tools, so the viewport owns its own keybindings; eager-resolved
        // in Boot before the keymap reload so its actions exist when the keymap builds.
        services.AddSingleton<WorldViewerToolbarActions>();

        // The active editing world (published by the World Viewer, read by the Hierarchy panel and the
        // entity ops), the shared entity edit verbs (copy/cut/paste/duplicate/delete/global/enabled, on the
        // current selection), and the Hierarchy panel's action registrations (Ctrl+C/X/V, Ctrl+D, Del, F2,
        // scoped to Scheme.For<WorldTreeViewModel>()) — eager-resolved in Boot before the keymap builds.
        services.AddSingleton<GameState>();
        services.AddSingleton<EntityOperations>();
        services.AddSingleton<WorldTreeActions>();

        // The viewport node overlay's per-project node layout (positions/collapse), keyed by world under the
        // project's .toybox/nodes folder. Shared by every open pane; the NodeGraphViewModel itself is built
        // fresh per pane by the ViewModelFactory.
        services.AddSingleton<NodeLayoutStore>();
        services.AddSingleton<NodeLayoutManager>();

        // The code editor's shared services: the loopback server that hosts the vendored Monaco bundle, the
        // open-document buffer store, and the open-into-Coder launcher (companion pairing + reuse-or-spawn).
        // The Coder panel view-model itself is built fresh by the ViewModelFactory on open, not a service.
        services.AddSingleton<MonacoAssetServer>();
        services.AddSingleton<ScriptService>();
        services.AddSingleton<CoderLauncher>();

        // The one type that holds the provider: it builds any view-model on demand, resolving the services
        // its constructor asks for and taking runtime arguments positionally. Every view-model is built
        // fresh — none is a service — so a panel that must survive close/reopen keeps its state in a service
        // the fresh view-model reads back (Settings ← SettingsManager, the Log Console ← the logger's
        // backlog), not in a kept-alive instance.
        services.AddSingleton<ViewModelFactory>();

        // The docking workspace: the catalog scans the feature assemblies for [Dockable] Views and builds
        // each panel's view-model through the ViewModelFactory on open — nothing here hand-wires a per-panel
        // factory. The world viewport dockable mints one editor viewport per pane itself and splits/joins
        // them Blender-style, disposing panes as they close. Popups is the app-wide modal service.
        services.AddSingleton(sp => new DockableCatalog(
            sp.GetRequiredService<ViewModelFactory>(),
            sp.GetRequiredService<Logger>(),
            typeof(WorldViewerView).Assembly,
            typeof(SettingsView).Assembly,
            typeof(AssetViewerView).Assembly,
            typeof(AssetBrowserView).Assembly,
            typeof(GameViewerView).Assembly,
            typeof(LogConsoleView).Assembly,
            typeof(CoderPanelView).Assembly,
            typeof(WorldTreeView).Assembly));
        services.AddSingleton<Popups>();

        // The docking workspace face (registered so the app-frame view-models and the asset-viewer
        // launcher share the one instance), the asset-open pipeline (the reuse-or-new launcher + the
        // type-router), and the in-process project switcher for File ▸ Open.
        services.AddSingleton<WorkspaceViewModel>();
        services.AddSingleton<AssetViewerLauncher>();
        services.AddSingleton<AssetOpener>();
        services.AddSingleton<ProjectSwitcher>();

        // The launch flow. Every dependency it takes is a service (the ViewModelFactory among them, which it
        // uses to build the window view-model graph it drives), so it registers like any other — no factory
        // lambda. (The project picker's view-model is the one construct built later, in the flow: it needs
        // the picker window's own storage provider for its Browse dialog.)
        services.AddSingleton<Launcher>();

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

        var log = services.GetRequiredService<Logger>();

        // Dock's own docking diagnostics flow into the studio log: a drag logs which drop control was
        // considered and why it was rejected, so a failing drag-and-drop names the check that rejected it
        // instead of failing silently. But Dock also traces every global-adorner add/remove/evaluate on
        // each drag — pure per-frame chatter that buried the console — so we drop the adorner lines and keep
        // the rest of the diagnostics.
        DockSettings.EnableDiagnosticsLogging = true;
        DockSettings.DiagnosticsLogHandler = message =>
        {
            if (message.Contains("adorner", StringComparison.OrdinalIgnoreCase))
                return;

            log.Info(message);
        };

        // From here on no crash is silent: UI exceptions log and are survived, fatal ones leave a
        // synchronous crash file beside the logs.
        CrashGuard.Install(log);

        // Theme loading runs before the logger exists, so its warnings are flushed once the logger does.
        var theme = services.GetRequiredService<ThemeManager>();
        theme.ApplySavedTheme();
        foreach (var warning in theme.LoadWarnings)
            log.Warning(warning);

        // Publish the motion tokens before any window exists: they gate EVERY animation (the splash's
        // rock/spin/nod and the micro-animation behaviors all read the AnimationIntensity resource, and
        // an unpublished token reads as 0 — motion off). The Settings window re-publishes live as the
        // intensity value is edited.
        MotionTokens.Publish(services.GetRequiredService<SettingsManager>().Editor.Accessibility.AnimationIntensity);

        // Purely event-driven services nobody injects — resolved so they exist and subscribe.
        // WorldViewerToolbarActions must exist before RunAsync's keymap reload so the world viewport's
        // action registrations are in; the tools it registers for exist to handle their invocations.
        services.GetRequiredService<SyncHub>();
        services.GetRequiredService<Gizmo>();
        services.GetRequiredService<WorldSelection>();
        services.GetRequiredService<WorldViewerTool>();
        services.GetRequiredService<RenderLayers>();
        services.GetRequiredService<WorldViewerToolbarActions>();
        services.GetRequiredService<WorldTreeActions>();
        services.GetRequiredService<AssetCatalog>();
        services.GetRequiredService<OwnedAppWatchdog>().Start();

        // Assets are constructed, not injected, so their lifecycle services are wired statically once.
        Asset.Configure(
            services.GetRequiredService<SyncHub>(), services.GetRequiredService<AssetCatalog>(), log);

        // Publish the context-menu system to the statics the low attach-behavior reaches: the catalog that
        // routes a right-clicked object to its menu, and the adapter that builds + shows the flyout.
        ContextMenuCatalog.Current = services.GetRequiredService<ContextMenuCatalog>();
        ContextMenuOpener.Current = services.GetRequiredService<ContextMenuOpenerAdapter>();
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

        // Stop the code editor's loopback asset server (its HttpListener). Any clangd it spawned was already
        // killed above with the Coder panel's view-model (DisposeWorkspacePanels).
        services.GetRequiredService<MonacoAssetServer>().Dispose();
        services.GetRequiredService<WorldViewerTool>().Dispose();
        services.GetRequiredService<RenderLayers>().Dispose();
        services.GetRequiredService<WorldSelection>().Dispose();
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
        // The keymap builds itself from the registered actions overlaid with the user's file; by
        // now the whole view-model graph (whose constructors register the actions) exists.
        _keymap.Reload();

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

            // Before anything tries to build or launch, make sure there is an engine to build against —
            // offering to download one if not (the splash owns the prompt, the main window isn't up yet).
            await EnsureEngineAsync().ContinueOnSameContext();

            if (_settings.Editor.Engine.AutoLaunchEngine)
            {
                await _coordinator.StartEngineAsync().ContinueOnSameContext();
            }
            else
            {
                _log.Info("Engine auto-launch is disabled in the editor settings; not launching.");
                _splashViewModel.Announce("Engine auto-launch is off.");
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
            _splashViewModel.Announce($"Launch failed: {exception.Message}");
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
    /// The startup engine check: if no engine checkout can be located (neither the settings path nor a
    /// checkout beside the project), offers to download one over the splash and, on yes, clones the
    /// configured engine repo (asking for its URL the first time) beside the project, then records the
    /// fetched checkout as the engine source path so the build finds it. Declining — or a failed
    /// download — is non-fatal: the studio still opens, and the user can point at (or fix) the engine
    /// path in Settings. Every branch narrates to the log.
    /// </summary>
    private async Task EnsureEngineAsync()
    {
        if (_engineLocator.Locate(_project.Path))
            return;

        _log.Warning("No Toybox engine checkout was found to build against.");
        _splashViewModel.Announce("No engine found.");

        var download = await _popups
            .ConfirmAsync(
                "Engine not found",
                "Toybox couldn't find an engine checkout to build against. Download it now with Git?",
                _splash)
            .ContinueOnSameContext();
        if (download != Confirmation.Yes)
        {
            _log.Info("Engine download declined; set the engine source path in Settings to build.");
            return;
        }

        var repoUrl = await ResolveEngineRepoUrlAsync().ContinueOnSameContext();
        if (repoUrl is null)
        {
            _log.Info("No engine repository URL was given; skipping the download.");
            return;
        }

        var target = EngineDownloadTarget();
        _splashViewModel.Announce("Downloading the engine…");

        // Drive the splash's activity bar for the download: announce the job so the bar shows with its
        // caption, feed git's transfer fraction into it, and clear it in a finally so a failure still
        // takes the bar down. This is the same LaunchActivity mechanism the build runner uses for compiles.
        var cloned = await WithActivityAsync(
            LaunchActivity.Downloading,
            progress => _git.CloneAsync(repoUrl, target, progress)).ContinueOnSameContext();
        if (!cloned)
        {
            _log.Error(cloned.Error!);
            _splashViewModel.Announce("Engine download failed.");
            await _popups
                .ErrorAsync(
                    "Engine download failed",
                    $"{cloned.Error}\n\nThe studio will open without an engine — check the URL and your "
                        + "connection, then download it again, or set the engine source path, from Settings.",
                    _splash)
                .ContinueOnSameContext();
            return;
        }

        // Remember both the URL used and where the checkout landed, so the next launch and every build
        // find the engine without asking again.
        _settings.Editor.Engine.RepoUrl = repoUrl;
        _settings.Editor.Engine.SourcePath = target;
        await _settings.ApplyAsync().ContinueOnSameContext();
        _log.Info($"Engine downloaded to '{target}'.");
    }

    /// <summary>
    /// Runs a launch <paramref name="activity"/> (a git transfer, here) with the splash's activity bar
    /// shown for its duration: dispatches the started/finished <see cref="LaunchActivityChanged"/> pair
    /// (the finish in a finally, so a failure still clears the bar) and hands the operation an
    /// <see cref="IProgress{T}"/> that feeds the bar via <see cref="LaunchActivityProgress"/>.
    /// </summary>
    private async Task<T> WithActivityAsync<T>(
        LaunchActivity activity, Func<IProgress<double>, Task<T>> operation)
    {
        var progress = new DelegateProgress<double>(
            fraction => _events.Dispatch(new LaunchActivityProgress(fraction)));

        _events.Dispatch(new LaunchActivityChanged(activity, true));
        try
        {
            return await operation(progress).ContinueOnSameContext();
        }
        finally
        {
            _events.Dispatch(new LaunchActivityChanged(activity, false));
        }
    }

    /// <summary>The engine repo URL to clone: the configured one, or asked for (and null when the user
    /// dismisses the prompt without giving one). The caller persists a freshly-entered URL on success.</summary>
    private async Task<string?> ResolveEngineRepoUrlAsync()
    {
        var configured = _settings.Editor.Engine.RepoUrl;
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var entered = await _popups
            .ShowAsync(
                _viewModels.Create<TextPromptPopupViewModel>(
                    "Engine repository", "https://…/Engine.git", "", false, "Download"),
                _splash)
            .ContinueOnSameContext();
        return string.IsNullOrWhiteSpace(entered) ? null : entered;
    }

    /// <summary>Where a downloaded engine checkout lands: an <c>Engine</c> folder beside the project (a
    /// Toybox root holds the engine alongside its projects), which is also where the locator's
    /// ancestor-climb looks.</summary>
    private string EngineDownloadTarget()
    {
        var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(_project.Path))?.FullName
            ?? _project.Path;
        return Path.Combine(parent, "Engine");
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

        // The splash is done: drop its event handlers and (in a Debug build) its log console's subscription
        // to the log stream, so nothing here outlives the handoff to the studio window.
        _splashViewModel.Dispose();
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
        // Settings/loader/factory resolve from the provider; the picker window's own storage provider (for
        // the Browse dialog) is the runtime argument.
        var viewModel = _viewModels.Create<ProjectPickerViewModel>(picker.StorageProvider);
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
        // Point the project-path registry at the newly opened project (its .toybox is where the layout,
        // node layouts, and project settings live), then read its project-scoped editor settings onto the
        // live settings before anything downstream reads them.
        _projectPaths.Root = _project.Path;
        _settings.LoadProjectSettings();
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
        RecentProjects.Remember(_settings.Editor.Projects, root);
        _settings.ApplyAsync().FireAndForget();
    }

    /// <summary>Disposes every open panel view-model (each viewport's engine view stops). Run first in
    /// shutdown, while the engine connection the disposals talk to is still up.</summary>
    internal void DisposeWorkspacePanels() => _mainViewModel.Workspace.DisposeInstances();

    /// <summary>Waits (bounded) for the Closing-time layout save's file write. The save never faults —
    /// the store logs and swallows its own I/O errors — so this only ever times out.</summary>
    internal void CompleteLayoutSave() => _layoutSave.Wait(LayoutSaveTimeout);
}
