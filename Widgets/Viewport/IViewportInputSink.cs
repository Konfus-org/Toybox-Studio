namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// Receives raw input captured from a viewport surface (by the <see cref="ViewportInput"/> behavior) and
/// forwards it to the engine view. Implemented by the surface view-model so the view stays free of any
/// engine/RPC knowledge.
/// </summary>
public interface IViewportInputSink
{
    /// <summary>Whether this surface shows the game (drives game input + the Esc/Alt+Esc focus model).</summary>
    bool IsGame { get; }

    /// <summary>
    /// Whether the playing game has requested relative-mouse (mouselook) mode. While true the game panel
    /// hides the OS cursor and re-centres it each move so look-deltas keep flowing without the pointer
    /// drifting out of the panel.
    /// </summary>
    bool RelativeMouse { get; }

    /// <summary>Forwards a snapshot of captured input to the engine view.</summary>
    void ForwardInput(ViewportInputPayload payload);

    /// <summary>Stops play mode (the game view's Esc). The engine and viewports keep running.</summary>
    void StopGame();
}
