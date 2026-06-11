using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Toybox.Studio.Services;
using Toybox.Studio.Shell;
using Toybox.Studio.Widgets.EngineConsole;
using Toybox.Studio.Widgets.EngineLauncher;
using Toybox.Studio.Widgets.EngineStatus;
using Toybox.Studio.Widgets.EngineViewport;
using Toybox.Studio.Widgets.EntityInspector;
using Toybox.Studio.Widgets.SceneTree;

namespace Toybox.Studio;

public partial class App : Application
{
    private IHost? _host;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Anchor configuration to the executable directory so launching from any working
            // directory still finds appsettings.json.
            var builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
            ConfigureServices(builder.Services, builder.Configuration);

            _host = builder.Build();
            _host.Start();

            desktop.MainWindow = new MainWindow
            {
                DataContext = _host.Services.GetRequiredService<ShellViewModel>(),
            };
            desktop.Exit += OnExit;

            if (desktop.Args?.Contains("--auto-launch") == true)
            {
                var session = _host.Services.GetRequiredService<EngineSessionService>();
                _ = session.LaunchAsync(CancellationToken.None);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var launchOptions = new EngineLaunchOptions();
        configuration.GetSection(EngineLaunchOptions.SectionName).Bind(launchOptions);
        services.AddSingleton(launchOptions);

        services.AddSingleton<EngineSessionService>();
        services.AddSingleton<EngineSceneService>();
        services.AddSingleton<SceneSelectionService>();
        services.AddSingleton<EngineViewportService>();

        services.AddSingleton<EngineStatusViewModel>();
        services.AddSingleton<EngineConsoleViewModel>();
        services.AddSingleton<EngineLauncherViewModel>();
        services.AddSingleton<SceneTreeViewModel>();
        services.AddSingleton<EntityInspectorViewModel>();
        services.AddSingleton<EngineViewportViewModel>();
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
            await host.Services.GetRequiredService<EngineSessionService>().DisposeAsync();
            await host.StopAsync(TimeSpan.FromSeconds(5));
        }).Wait(TimeSpan.FromSeconds(15));
        host.Dispose();
    }
}
