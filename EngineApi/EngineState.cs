using Toybox.Studio.Hosting;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// The run states an external caller may ask the engine to enter, through the one door
/// <see cref="Engine.SetStateAsync"/> — which also enforces the valid transitions between them
/// (play from editing, pause only while playing, resume or stop from paused, shut down from anywhere).
/// </summary>
public enum EngineRunState
{
    /// <summary>Editor mode: the world is being edited, no game loop runs. Requesting it exits play.</summary>
    Editing,

    /// <summary>Play mode, running the game loop. Requested from editing (enter play) or paused (resume).</summary>
    Playing,

    /// <summary>Play mode with the simulation paused.</summary>
    Paused,

    /// <summary>The engine process exits gracefully.</summary>
    Shutdown,
}

/// <summary>The engine's coarse activity phase — what it is doing right now.</summary>
public enum EnginePhase
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

/// <summary>
/// The single "what is the engine doing right now" value the UI watches: the activity phase, whether
/// the host owns or attached to the process, the loading-phase text the ghost shows, whether a load is
/// a play transition (so only the game viewport shows that ghost), and whether the simulation is
/// paused. Derived and held by
/// <see cref="Engine"/> (read it as <see cref="Engine.State"/>) and dispatched whole as
/// <see cref="EngineStateChanged"/>, so every consumer reads one coherent snapshot instead of stitching
/// separate state bits together.
/// </summary>
public readonly record struct EngineState(
    EnginePhase Phase,
    HostKind Kind = HostKind.None,
    string StatusMessage = "",
    bool IsGameLoading = false,
    bool IsPaused = false)
{
    /// <summary>No engine: the initial and torn-down state.</summary>
    public static EngineState Off => new(EnginePhase.Off);

    /// <summary>Connected with a live frame — editing or playing.</summary>
    public bool IsConnected => Phase is EnginePhase.Ready or EnginePhase.Playing;

    /// <summary>Compile/launch/load work is in flight and nothing usable is on screen yet.</summary>
    public bool IsLoading => Phase is EnginePhase.Compiling or EnginePhase.Loading;
}
