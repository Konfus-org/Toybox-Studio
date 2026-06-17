namespace Toybox.Studio.Services.EngineApi;

/// <summary>
/// Connection lifecycle between the editor and the engine process.
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Launching,
    Connected,
}
