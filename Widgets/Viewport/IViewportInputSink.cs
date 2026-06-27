using System.Threading.Tasks;

namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// What a viewport exposes to the shared <see cref="ViewportInput"/> behavior: raw input forwarding plus
/// a few capability flags and generic pointer/keyboard gestures. The behavior is deliberately kind-blind —
/// it never asks "is this the game?". Each specific viewport (editor / game / asset preview) declares its
/// capabilities and decides what a tap, a marquee, a right-tap, or Escape means (often a no-op).
/// </summary>
public interface IViewportInputSink
{
    /// <summary>Forwards a snapshot of captured input to the engine view (camera fly / game input / orbit).</summary>
    void ForwardInput(ViewportInputPayload payload);

    /// <summary>
    /// Whether the view wants the OS cursor pinned to the panel centre for relative-mouse (mouselook):
    /// while true the behavior hides + re-centres the cursor each move so look-deltas keep flowing. The
    /// game sets this during play; other viewports leave it false.
    /// </summary>
    bool WantsPointerLock { get; }

    /// <summary>Whether a left-drag should begin a rubber-band box-select (an editor viewport with the
    /// select tool active). False disables the marquee so a left-drag is just forwarded input.</summary>
    bool AllowsMarquee { get; }

    /// <summary>Whether a right-tap (a right press that didn't become a camera pan) opens the entity /
    /// background context menu. False on the game view.</summary>
    bool AllowsContextMenu { get; }

    /// <summary>
    /// A left click that did not become a drag. The point is in control space; the view letterbox-maps it
    /// to the rendered image and acts (editor/preview pick + select; <paramref name="additive"/> toggles).
    /// A no-op for the game view.
    /// </summary>
    void Tap(double x, double y, double width, double height, bool additive);

    /// <summary>Shows/updates the rubber-band marquee rectangle (control space) while a left-drag is in
    /// progress (only ever called when <see cref="AllowsMarquee"/>).</summary>
    void UpdateMarquee(double x, double y, double width, double height);

    /// <summary>Commits a marquee box-select on drag release: the rect is letterbox-mapped to the rendered
    /// image and the enclosed entities replace (or, when <paramref name="additive"/>, add to) the
    /// selection. Also hides the marquee.</summary>
    void EndMarquee(
        double x, double y, double width, double height,
        double controlWidth, double controlHeight, bool additive);

    /// <summary>Hides the marquee without selecting (e.g. the viewport lost focus mid-drag).</summary>
    void CancelMarquee();

    /// <summary>
    /// Plain <b>Esc</b> on the focused viewport. The view does its thing and returns true if it consumed
    /// the key (the game stops play); false lets it bubble. (<b>Alt+Esc</b> is the behavior's own universal
    /// "release viewport focus" shortcut and never reaches the sink.)
    /// </summary>
    bool HandleEscape();

    /// <summary>
    /// A right-tap (a right press that didn't become a camera pan): picks the entity at the point and selects
    /// it (non-additive), so the context menu the behavior then opens targets what was clicked. The point is in
    /// control space. Returns the hit entity id (null on a miss → the behavior opens the background menu). A
    /// no-op returning null where picking doesn't apply (the game view). Only called when
    /// <see cref="AllowsContextMenu"/> is true.
    /// </summary>
    Task<ulong?> PickAndSelectForMenuAsync(double x, double y, double controlWidth, double controlHeight);
}
