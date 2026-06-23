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

    /// <summary>
    /// Picks the entity at a viewport click (a left click that wasn't a drag). The point is in control space;
    /// the sink converts it to the rendered image via the letterbox and asks the engine. When
    /// <paramref name="additive"/> (Shift) the hit toggles in/out of the selection; otherwise it replaces it
    /// (an empty-space click clears). A no-op for the game view.
    /// </summary>
    void Pick(double pointerX, double pointerY, double controlWidth, double controlHeight, bool additive);

    /// <summary>Shows/updates the rubber-band marquee rectangle (control space) while a left-drag is in
    /// progress, so the view can draw it.</summary>
    void UpdateMarquee(double x, double y, double width, double height);

    /// <summary>
    /// Commits a marquee box-select on drag release: the rect is in control space and is letterbox-mapped
    /// to the rendered image, then the enclosed entities replace (or, when <paramref name="additive"/>, add
    /// to) the selection. Also hides the marquee.
    /// </summary>
    void EndMarquee(
        double x, double y, double width, double height,
        double controlWidth, double controlHeight, bool additive);

    /// <summary>Hides the marquee without selecting (e.g. the viewport lost focus mid-drag).</summary>
    void CancelMarquee();

    /// <summary>Whether a left-drag should box-select (true only with the select tool active; a transform
    /// tool reserves the left-drag for its gizmo).</summary>
    bool MarqueeEnabled { get; }

    /// <summary>Stops play mode (the game view's Esc). The engine and viewports keep running.</summary>
    void StopGame();
}
