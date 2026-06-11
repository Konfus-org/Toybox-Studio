namespace Toybox.Studio.Services;

/// <summary>Connection lifecycle between the editor and the engine process.</summary>
public enum EngineConnectionState
{
    Disconnected,
    Launching,
    Connected,
}
