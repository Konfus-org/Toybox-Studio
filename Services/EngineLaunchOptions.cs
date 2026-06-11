namespace Toybox.Studio.Services;

/// <summary>How to start the engine process; bound from the "Engine" section of appsettings.json.</summary>
public sealed class EngineLaunchOptions
{
    public const string SectionName = "Engine";

    public string LauncherPath { get; set; } = "";

    public string WorkingDirectory { get; set; } = "";

    /// <summary>How long to keep retrying the RPC connection while the engine boots.</summary>
    public int ConnectTimeoutSeconds { get; set; } = 30;

    /// <summary>Launch the engine headless (--headless) so the studio viewport is the view.</summary>
    public bool HideEngineWindow { get; set; } = true;

    /// <summary>Relaunch the engine when it exits abnormally (with a crash-loop guard).</summary>
    public bool RestartOnCrash { get; set; } = true;
}
