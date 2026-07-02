using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
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
/// The composition root and startup flow, kept deliberately linear: build the services, show the
/// splash (a loading bar with playful phase lines), prompt for the project to open, compile + launch
/// it, and only then create and show the main window with the 3D viewport.
/// </summary>
public partial class App : Application
{
    // Minimum time the splash stays up so a fast startup doesn't flash.
    private static readonly TimeSpan SplashMinimumDuration = TimeSpan.FromMilliseconds(900);

    private Services? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = new Services();
            desktop.Exit += (_, _) => _services.Shutdown();

            // Nothing owns the app's lifetime until the studio window exists (it is only created once
            // startup finishes), so shutdown stays explicit for now — closing the splash mid-startup
            // must not end the app. StartupAsync hands the lifetime to the studio window when it opens.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var splash = new SplashWindow { DataContext = _services.Splash };
            splash.Show();
            StartupAsync(desktop, splash).FireAndForget();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartupAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash)
    {
        var services = _services!;
        var timer = Stopwatch.StartNew();

        // Startup touches windows, so every await must resume back on the UI thread.
        try
        {
            if (await PromptForProjectAsync(splash, services).ContinueOnSameContext() is not { } project)
            {
                services.Log.Error("No project was chosen, so there is nothing to launch.");
                services.Splash.Status = "No project chosen.";
            }
            else if (services.Settings.Settings.Engine.AutoLaunchEngine)
            {
                // Compiles if needed, launches the engine process, and returns once connected (or failed —
                // failures land in the log). The splash narrates the phases from the dispatched engine
                // state; the viewport starts streaming on connect by itself.
                await services.Coordinator.StartEngineAsync(project).ContinueOnSameContext();
            }
            else
            {
                services.Log.Info("Engine auto-launch is disabled in the editor settings; not launching.");
                services.Splash.Status = "Engine auto-launch is off.";
            }

            var remaining = SplashMinimumDuration - timer.Elapsed;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining).ContinueOnSameContext();
        }
        catch (Exception exception)
        {
            services.Log.Error($"Startup failed: {exception.Message}");
            services.Splash.Status = $"Startup failed: {exception.Message}";
            await Task.Delay(TimeSpan.FromSeconds(3)).ContinueOnSameContext();
        }

        // Setup is done (or failed past the splash): only now does the studio window come to exist.
        // Show it, hand it the app's lifetime, then swap the splash out for it.
        var mainWindow = new MainWindow { DataContext = services.Shell };
        desktop.MainWindow = mainWindow;
        mainWindow.Show();
        desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        splash.Close();
        mainWindow.Activate();
    }

    /// <summary>
    /// Asks the user which project folder to open, starting the picker at the last-opened project (there
    /// is no project-picker UI yet, so every launch asks). Re-prompts while the picked folder isn't a
    /// project; null once cancelled. A valid pick is remembered in the editor settings.
    /// </summary>
    private static async Task<Project?> PromptForProjectAsync(SplashWindow splash, Services services)
    {
        var settings = services.Settings.Settings;
        var lastOpened = settings.Projects.LastOpened;
        var startLocation = lastOpened.Length > 0
            ? await splash.StorageProvider.TryGetFolderFromPathAsync(lastOpened).ContinueOnSameContext()
            : null;

        while (true)
        {
            var picks = await splash.StorageProvider.OpenFolderPickerAsync(
                    new FolderPickerOpenOptions
                    {
                        Title = "Open a Toybox project",
                        AllowMultiple = false,
                        SuggestedStartLocation = startLocation,
                    })
                .ContinueOnSameContext();
            if (picks.Count == 0 || picks[0].TryGetLocalPath() is not { } root)
                return null;

            if (Project.IsProjectDirectory(root))
            {
                RememberProject(services, root);
                return new Project(root, settings.Engine.SourcePath, services.Log, services.Events);
            }

            services.Log.Warning(
                $"'{root}' is not a Toybox project (it has no {Project.SettingsFileName}); pick another folder.");
        }
    }

    /// <summary>Records the opened project as the last-opened and front of the recents (capped) list.</summary>
    private static void RememberProject(Services services, string root)
    {
        const int maxRecent = 10;

        var projects = services.Settings.Settings.Projects;
        projects.LastOpened = root;
        projects.Recent.RemoveAll(p => string.Equals(p, root, StringComparison.OrdinalIgnoreCase));
        projects.Recent.Insert(0, root);
        if (projects.Recent.Count > maxRecent)
            projects.Recent.RemoveRange(maxRecent, projects.Recent.Count - maxRecent);

        services.Settings.SaveAsync().FireAndForget();
    }

    /// <summary>
    /// Every service the app is composed of, wired by hand in dependency order — no DI container, no
    /// registration indirection; what talks to what is exactly what this constructor says.
    /// </summary>
    private sealed class Services
    {
        public Services()
        {
            // Settings first — nearly everything below reads them — then the theme, applied before any
            // window exists so even the splash renders themed. Theme loading runs before the logger, so
            // its warnings are flushed once the logger exists.
            Settings = new SettingsManager();
            Theme = new ThemeManager(Settings);
            Theme.ApplySavedTheme();

            // Logging next, so every later step lands in TbxStudio.log and the console (which the
            // splash and any console panel both show); the engine console's colours track the active
            // theme via the ThemeLogSource adapter. Then the event bus every domain signal flows
            // through — publishers dispatch typed structs, handlers register; nobody holds anybody.
            var logFile = new LogFile();
            Log = new Logger(logFile, new ThemeLogSource(Theme));
            foreach (var warning in Theme.LoadWarnings)
                Log.Warning(warning);
            Console = new ConsoleViewModel();
            Log.Logged += entry => Console.Append(new ConsoleLine(entry.Message, SeverityOf(entry)));
            Events = new EventDispatcher();

            // The engine and its host: the engine service is what everything talks to (and reads State
            // from); the host itself only ever launches/attaches/stops what it is handed. Which project
            // drives the launch is startup's business — it prompts for one and hands it to the coordinator.
            var engineSettings = Settings.Settings.Engine;
            Engine = new Engine(Log, Events);
            Host = new AppHost<Engine>(Engine, Log, Events, engineSettings.RestartOnCrash);

            // Generic owned-app supervision: ping the connected engine so a freeze is noticed, and while
            // disconnected watch for an engine that is already running (e.g. launched by a debugger) to
            // attach to instead of launching a second one. The coordinator decides what to do with what
            // it reports.
            _ownedAppWatchdog = new OwnedAppWatchdog(
                Engine, EngineCommands.EnginePing, EngineApi.Engine.DefaultPort, Events);
            _ownedAppWatchdog.Start();
            Coordinator = new EngineCoordinator(
                Host,
                _ownedAppWatchdog,
                engineSettings.HideEngineWindow,
                engineSettings.ConnectTimeoutSeconds,
                Log,
                Events);

            // The one 3D view: an editor-camera stream into the engine's world, filling the main window.
            Viewport = new ViewportViewModel(Events, Log, ViewKind.Editor);
            Viewport.Prepare(new ViewportStream(Engine, Events, ViewKind.Editor));

            Shell = new ShellViewModel(new StatusViewModel(Events), Viewport);
            Splash = new SplashViewModel(Events);
        }

        public SettingsManager Settings { get; }
        public ThemeManager Theme { get; }
        public Logger Log { get; }
        public ConsoleViewModel Console { get; }
        public EventDispatcher Events { get; }
        public Engine Engine { get; }
        public AppHost<Engine> Host { get; }
        public EngineCoordinator Coordinator { get; }
        public ViewportViewModel Viewport { get; }
        public ShellViewModel Shell { get; }
        public SplashViewModel Splash { get; }

        // Kept alive for the app's lifetime; it works entirely through events.
        private readonly OwnedAppWatchdog _ownedAppWatchdog;

        /// <summary>Stops the hosted engine (gracefully, killing on timeout) as the app exits.</summary>
        public void Shutdown()
        {
            Coordinator.Dispose();
            _ownedAppWatchdog.Dispose();
            Viewport.Dispose();
            // Run the async teardown on the thread pool: blocking the UI thread on code that resumes
            // via its SynchronizationContext would deadlock.
            Task.Run(async () => await Host.DisposeAsync().ContinueOnAnyContext())
                .Wait(TimeSpan.FromSeconds(15));
        }

        private static ConsoleSeverity SeverityOf(LogEntry entry) =>
            entry.IsError ? ConsoleSeverity.Error
            : entry.IsWarning ? ConsoleSeverity.Warning
            : ConsoleSeverity.Normal;
    }
}
