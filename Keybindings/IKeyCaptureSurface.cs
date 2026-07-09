namespace Toybox.Studio.Keybindings;

/// <summary>
/// A control that records key chords (the keybindings page's capture box). While one is capturing,
/// the <see cref="KeybindingDispatcher"/> stands down entirely — the pressed chord is being
/// ASSIGNED, so it must reach the control instead of invoking whatever it is currently bound to.
/// </summary>
public interface IKeyCaptureSurface
{
    bool IsCapturingKeys { get; }
}
