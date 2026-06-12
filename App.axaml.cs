using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Toybox.Studio.Services;
using Toybox.Studio.Shell;
using Toybox.Studio.Widgets.LogConsole;
using Toybox.Studio.Widgets.Status;
using Toybox.Studio.Widgets.Viewport;
using Toybox.Studio.Widgets.EntityInspector;
using Toybox.Studio.Widgets.GameToolbar;
using Toybox.Studio.Widgets.WorldTree;

namespace Toybox.Studio;

public partial class App : Application
{
    // Minimum time the splash stays up so a fast startup doesn't flash; the brief pause between steps
    // lets each line of the splash log actually render.
    private static readonly TimeSpan SplashMinimumDuration = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan StepPause = TimeSpan.FromMilliseconds(140);

    private IHost? _host;

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
            _host!.Services.GetRequiredService<ThemeManager>().ApplySavedTheme();

            await StepAsync("Building workspace…").ContinueOnSameContext();
            // The shell (and its console widget) must exist before discovery so startup log
            // lines land in the console.
            var mainWindow = new MainWindow
            {
                DataContext = _host.Services.GetRequiredService<ShellViewModel>(),
                IsVisible = false, // Revealed only after the splash closes.
            };
            desktop.MainWindow = mainWindow;
            // Closing the splash must not end the app; tie the lifetime to the main window instead.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.Exit += OnExit;

            await StepAsync("Locating engine…").ContinueOnSameContext();
            var session = _host.Services.GetRequiredService<EngineSession>();
            var locator = _host.Services.GetRequiredService<EngineLocator>();
            var locatorReport = locator.ResolveAtStartup();
            log.Info(locatorReport);
            splashViewModel.Status =
                locator.IsLocated ? "Engine located." : "Engine not found — set it in Settings.";

            await StepAsync("Watching for running engines…").ContinueOnSameContext();
            var detector = _host.Services.GetRequiredService<EngineInstanceDetector>();
            detector.InstanceDetected += port => session.AttachAsync(port).FireAndForget();
            detector.Start();

            // Launch into a world at startup (the default). With no project open, fall back to the
            // bundled template world (skybox + grass ground + a cube that falls). The compile/launch
            // runs in the background so the editor opens immediately.
            var settings = _host.Services.GetRequiredService<Settings>();
            var projects = _host.Services.GetRequiredService<ProjectManager>();
            var wantsLaunch = settings.Editor.Engine.AutoLaunchEngine
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
                MessageBoxWindow.ShowAsync(
                    mainWindow,
                    "Engine Not Found",
                    "The Toybox Engine source folder could not be located automatically. "
                    + "Open Settings (⚙) and point the Engine path at your engine checkout.").FireAndForget();
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

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<Settings>();
        services.AddSingleton<ThemeManager>();
        services.AddSingleton<LogFile>();
        services.AddSingleton<Logger>();
        services.AddSingleton<ProjectManager>();
        services.AddSingleton<FilePicker>();
        services.AddSingleton<CMakeCompiler>();
        services.AddSingleton<EngineJsonParser>();
        services.AddSingleton<WorldSelection>();
        services.AddSingleton<EngineSession>();
        services.AddSingleton<EngineInstanceDetector>();
        services.AddSingleton<WorldManager>();
        services.AddSingleton<ViewportStream>();
        services.AddSingleton<EngineLocator>();
        services.AddSingleton<StatusViewModel>();
        services.AddSingleton<LogConsoleViewModel>();
        services.AddSingleton<WorldTreeViewModel>();
        services.AddSingleton<EntityInspectorViewModel>();
        services.AddSingleton<ViewportViewModel>();
        services.AddSingleton<GameToolbarViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ShellViewModel>();
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (_host is null)
            return;

        // Run the async teardown on the thread pool: blocking the UI thread on code that
        // resumes via its SynchronizationContext would deadlock.
        var host = _host;
        _host = null;
        Task.Run(async () =>
        {
            await host.Services.GetRequiredService<EngineSession>().DisposeAsync().ContinueOnAnyContext();
            await host.StopAsync(TimeSpan.FromSeconds(5)).ContinueOnAnyContext();
        }).Wait(TimeSpan.FromSeconds(15));
        host.Dispose();
    }
}
