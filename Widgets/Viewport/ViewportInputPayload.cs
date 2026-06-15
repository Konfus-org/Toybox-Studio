using System.Collections.Generic;

namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// One snapshot of viewport input forwarded to the engine. Editor views use <see cref="MoveKeys"/> for
/// the fly camera; game views use <see cref="Keys"/> (SDL scancodes) and <see cref="MouseX"/>/
/// <see cref="MouseY"/> for the game input system. Mouse/wheel values are deltas since the last call.
/// </summary>
public sealed record ViewportInputPayload(
    bool Focused,
    int Buttons,
    int MoveKeys,
    IReadOnlyList<int> Keys,
    double MouseX,
    double MouseY,
    double Dx,
    double Dy,
    double Wheel);
