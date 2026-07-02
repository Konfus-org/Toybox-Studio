namespace Toybox.Studio.EngineApi;

/// <summary>
/// The single, coarse "what is the engine doing right now" state that the UI watches, derived by
/// <see cref="EngineWatcher"/> from the lower-level session and engine signals.
/// </summary>
public enum EngineState
{
    /// <summary>No engine: disconnected and idle.</summary>
    Off,

    /// <summary>The project is being compiled.</summary>
    Compiling,

    /// <summary>Launching/connecting, or transitioning into play — work is in flight and no usable
    /// frame is on screen yet.</summary>
    Loading,

    /// <summary>Connected in editor mode with a live frame; not playing.</summary>
    Ready,

    /// <summary>Connected and running the game loop (play mode).</summary>
    Playing,
}
