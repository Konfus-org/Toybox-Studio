using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Toybox.Studio.CMake;
using Toybox.Studio.Console;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Shell;
using Toybox.Studio.Status;
using Toybox.Studio.AppHosting;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Extensions;
using Toybox.Studio.Viewport;

namespace Toybox.Studio;

/// <summary>
/// The composition root and startup flow, kept deliberately linear: build the services, show the
/// splash (a loading bar with playful phase lines), locate the engine, compile + launch the example
/// project, and only then create and show the main window with the 3D viewport.
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
            services.Log.Info(services.Locator.ResolveAtStartup());

            if (services.Project.Current is not null)
            {
                // Compiles if needed, launches the engine process, and returns once connected (or failed —
                // failures land in the log). The splash narrates the phases from the dispatched engine
                // state; the viewport starts streaming on connect by itself.
                await services.Coordinator.StartEngineAsync().ContinueOnSameContext();
            }
            else
            {
                services.Log.Error(
                    "No engine/example project found. Place the studio next to your Toybox checkout "
                        + "(Engine + ExampleProject) and restart.");
                services.Splash.Status = "Engine not found.";
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
    /// Every service the app is composed of, wired by hand in dependency order — no DI container, no
    /// registration indirection; what talks to what is exactly what this constructor says.
    /// </summary>
    private sealed class Services
    {
        public Services()
        {
            // Logging first, so every later step lands in TbxStudio.log and the console (which the
            // splash and any console panel both show). Then the event bus every domain signal flows
            // through — publishers dispatch typed structs, handlers register; nobody holds anybody.
            var logFile = new LogFile();
            Log = new Logger(logFile, new StaticLogTheme());
            Console = new ConsoleViewModel();
            Log.Logged += entry => Console.Append(new ConsoleLine(entry.Message, SeverityOf(entry)));
            var events = new EventDispatcher();

            // The engine and its host: locate the engine source, expose the example project beside it
            // (which builds itself via the generic CMake driver), and host the engine process. The
            // coordinator ties them together — the host itself only ever launches/attaches/stops what
            // it is handed, and the engine service is what everything talks to (and reads State from).
            var settings = StudioSettings.Load();
            Locator = new EngineLocator(settings, events);
            Project = new ExampleProject(Locator, new CMakeCompiler(new CommandRunner(Log), Log), Log, events);
            Engine = new Engine(Log, events);
            Host = new AppHost<Engine>(Engine, Log, events, settings.RestartOnCrash);

            // Generic owned-app supervision: ping the connected engine so a freeze is noticed, and while
            // disconnected watch for an engine that is already running (e.g. launched by a debugger) to
            // attach to instead of launching a second one. The coordinator decides what to do with what
            // it reports.
            _ownedAppWatchdog = new OwnedAppWatchdog(
                Engine, EngineCommands.EnginePing, EngineApi.Engine.DefaultPort, events);
            _ownedAppWatchdog.Start();
            Coordinator = new EngineCoordinator(Project, Host, _ownedAppWatchdog, settings, Log, events);

            // The one 3D view: an editor-camera stream into the engine's world, filling the main window.
            Viewport = new ViewportViewModel(events, Log, ViewKind.Editor);
            Viewport.Prepare(new ViewportStream(Engine, events, ViewKind.Editor));

            Shell = new ShellViewModel(new StatusViewModel(events), Viewport);
            Splash = new SplashViewModel(events);
        }

        public Logger Log { get; }
        public ConsoleViewModel Console { get; }
        public EngineLocator Locator { get; }
        public ExampleProject Project { get; }
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
