using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Toybox.Studio.AppHosting;
using Toybox.Studio.Console;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Projects;
using Toybox.Studio.Settings;
using Toybox.Studio.Shell;
using Toybox.Studio.Status;
using Toybox.Studio.Themes;
using Toybox.Studio.Utils;
using Toybox.Studio.Viewport;

namespace Toybox.Studio;

/// <summary>
/// The app's bootstrap and composition root. <see cref="LaunchAsync"/> is the one endpoint: it boots
/// Avalonia, configures the service provider (every service registered against exactly what it asks
/// for in its constructor — nothing ever takes the provider itself), and runs the startup flow: the
/// splash with the project picker over it, the picked project's compile + engine launch, then the
/// studio window, which takes over the app's lifetime.
/// </summary>
public sealed class Launcher
{
    // Minimum time the splash stays up so a fast launch doesn't flash.
    private static readonly TimeSpan SplashMinimumDuration = TimeSpan.FromMilliseconds(900);

    // How long app exit waits for the hosted engine's graceful stop before it is killed.
    private static readonly TimeSpan EngineTeardownTimeout = TimeSpan.FromSeconds(15);

    private readonly SettingsManager _settings;
    private readonly Project _project;
    private readonly ProjectLoader _projectLoader;
    private readonly EngineCoordinator _coordinator;
    private readonly Logger _log;
    private readonly MainWindowViewModel _mainViewModel;
    private readonly SplashViewModel _splashViewModel;
    private readonly SplashWindow _splash;

    public Launcher(
        SettingsManager settings,
        Project project,
        ProjectLoader projectLoader,
        EngineCoordinator coordinator,
        Logger log,
        SplashViewModel splashViewModel,
        MainWindowViewModel mainViewModel)
    {
        _settings = settings;
        _project = project;
        _projectLoader = projectLoader;
        _coordinator = coordinator;
        _log = log;
        _mainViewModel = mainViewModel;
        _splashViewModel = splashViewModel;
        _splash = new SplashWindow { DataContext = splashViewModel };
    }

    [STAThread]
    public static void Main(string[] args) => LaunchAsync(args).GetAwaiter().GetResult();

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

        // Unified logging: TbxStudio.log plus the console view, with the engine console's colours
        // tracking the active theme via the ThemeLogSource adapter.
        services.AddSingleton(sp =>
            new Logger(new LogFile(), new ThemeLogSource(sp.GetRequiredService<ThemeManager>())));
        services.AddSingleton<ConsoleViewModel>();

        // The event bus every domain signal flows through — publishers dispatch typed structs,
        // handlers register; nobody holds anybody.
        services.AddSingleton<EventDispatcher>();

        // The active project — pure data, populated by the launch flow through the loader once the
        // user picks one — and the build/ship services callers compose around it. The builder gets
        // its settings knob (the configured engine source path) as a plain value.
        services.AddSingleton<Project>();
        services.AddSingleton<ProjectLoader>();
        services.AddSingleton(sp => new ProjectBuilder(
            sp.GetRequiredService<SettingsManager>().Editor.Engine.SourcePath,
            sp.GetRequiredService<Logger>(),
            sp.GetRequiredService<EventDispatcher>()));
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

        // The one 3D view: an editor-camera stream into the engine's world, filling the main window.
        services.AddSingleton(sp =>
        {
            var viewport = new ViewportViewModel(
                sp.GetRequiredService<EventDispatcher>(), sp.GetRequiredService<Logger>(), ViewKind.Editor);
            viewport.Prepare(new ViewportStream(
                sp.GetRequiredService<Engine>(), sp.GetRequiredService<EventDispatcher>(), ViewKind.Editor));
            return viewport;
        });

        // The window view-models and the launch flow itself. (The project picker's view-model is the
        // one construct built outside the container, in the flow: it needs the picker window's own
        // storage provider for its Browse dialog.)
        services.AddSingleton<StatusViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SplashViewModel>();
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
        // Theme loading runs before the logger exists, so its warnings are flushed once the logger does.
        var theme = services.GetRequiredService<ThemeManager>();
        theme.ApplySavedTheme();

        var log = services.GetRequiredService<Logger>();
        foreach (var warning in theme.LoadWarnings)
            log.Warning(warning);

        var console = services.GetRequiredService<ConsoleViewModel>();
        log.Logged += entry => console.Append(new ConsoleLine(entry.Message, SeverityOf(entry)));

        // Purely event-driven services nobody injects — resolved so they exist and subscribe.
        services.GetRequiredService<SyncHub>();
        services.GetRequiredService<OwnedAppWatchdog>().Start();
    }

    /// <summary>
    /// Stops the hosted engine (gracefully, killing on timeout) as the app exits. Teardown order is
    /// engine-critical, so it stays explicit rather than leaning on the provider's reverse-creation-order
    /// disposal (which also couldn't await the host); the process exits right after, so the provider
    /// itself is left undisposed.
    /// </summary>
    private static void Shutdown(ServiceProvider services)
    {
        services.GetRequiredService<EngineCoordinator>().Dispose();
        services.GetRequiredService<OwnedAppWatchdog>().Dispose();
        services.GetRequiredService<SyncHub>().Dispose();
        services.GetRequiredService<ViewportViewModel>().Dispose();
        // Run the async teardown on the thread pool: blocking the UI thread on code that resumes
        // via its SynchronizationContext would deadlock.
        Task.Run(async () =>
                await services.GetRequiredService<AppHost<Engine>>().DisposeAsync().ContinueOnAnyContext())
            .Wait(EngineTeardownTimeout);
    }

    /// <summary>
    /// The interactive startup flow, run inside the UI loop: show the splash, prompt for the project,
    /// compile it and launch its engine (failures land in the log and on the splash; the splash narrates
    /// the phases from the dispatched engine state), then swap the splash out for the studio window.
    /// Touches windows, so every await resumes back on the UI thread.
    /// </summary>
    private async Task RunAsync(ClassicDesktopStyleApplicationLifetime desktop)
    {
        _splash.Show();
        var timer = Stopwatch.StartNew();

        try
        {
            if (!await PromptForProjectAsync().ContinueOnSameContext())
            {
                _log.Info("No project was chosen; continuing without one.");
                _splashViewModel.Status = "No project chosen.";
            }
            else if (_settings.Editor.Engine.AutoLaunchEngine)
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
            _splashViewModel.Status = $"Launch failed: {exception.Message}";
            await Task.Delay(TimeSpan.FromSeconds(3)).ContinueOnSameContext();
        }

        // Launch is done (or failed past the splash): only now does the studio window come to exist.
        // Show it, hand it the app's lifetime, then swap the splash out for it.
        var mainWindow = new MainWindow { DataContext = _mainViewModel };
        desktop.MainWindow = mainWindow;
        mainWindow.Show();
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        _splash.Close();
        mainWindow.Activate();
    }

    /// <summary>
    /// Asks the user which project to open with the VS-style picker: the recent projects to reopen,
    /// plus browsing the disk for one. A valid pick is remembered in the editor settings and loaded
    /// into the active project; false when the user continues without a project (or just closes the
    /// picker), leaving the active project empty.
    /// </summary>
    private async Task<bool> PromptForProjectAsync()
    {
        var picker = new ProjectPickerWindow();
        var viewModel = new ProjectPickerViewModel(
            _settings.Editor.Projects.Recent, _projectLoader, picker.StorageProvider);
        picker.DataContext = viewModel;
        // Closing the window without choosing (the title-bar X) counts as continuing without a project.
        picker.Closed += (_, _) => viewModel.SkipCommand.Execute(null);
        picker.Show(_splash);

        var root = await viewModel.Choice.ContinueOnSameContext();
        picker.Close();
        if (root is null)
            return false;

        RememberProject(root);
        _projectLoader.Load(root, _project);
        return true;
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

    private static ConsoleSeverity SeverityOf(LogEntry entry) =>
        entry.IsError ? ConsoleSeverity.Error
        : entry.IsWarning ? ConsoleSeverity.Warning
        : ConsoleSeverity.Normal;
}
