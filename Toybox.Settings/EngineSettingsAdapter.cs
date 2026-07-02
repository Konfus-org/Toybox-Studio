using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Settings;

/// <summary>
/// Adapts the editor <see cref="SettingsManager"/> to the engine layer's <see cref="IEngineSettings"/>,
/// exposing just the engine-launch settings the core engine connection needs.
/// </summary>
public sealed class EngineSettingsAdapter : IEngineSettings
{
    private readonly SettingsManager _settings;

    public EngineSettingsAdapter(SettingsManager settings) => _settings = settings;

    public string SourcePath
    {
        get => _settings.Settings.Engine.SourcePath;
        set => _settings.Settings.Engine.SourcePath = value;
    }

    public bool HideEngineWindow => _settings.Settings.Engine.HideEngineWindow;

    public int ConnectTimeoutSeconds => _settings.Settings.Engine.ConnectTimeoutSeconds;

    public bool RestartOnCrash => _settings.Settings.Engine.RestartOnCrash;

    public Task SaveAsync() => _settings.SaveAsync();
}
