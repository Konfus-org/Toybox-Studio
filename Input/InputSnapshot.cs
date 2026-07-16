using Avalonia.Input;
using Avalonia;

namespace Toybox.Studio.Input;

/// <summary>
/// One snapshot of captured user input, expressed in the UI framework's own strongly-typed input
/// vocabulary (<see cref="Key"/>, <see cref="MouseButton"/>, <see cref="Point"/>) — never in any
/// consumer's wire format. A consumer that speaks something else (an owned app's process, say)
/// translates on its own side.
/// </summary>
/// <param name="Focused">Whether the input surface has keyboard focus.</param>
/// <param name="Keys">Every key currently held.</param>
/// <param name="Buttons">Every pointer button currently held.</param>
/// <param name="PointerPosition">The pointer position in the input surface's control space.</param>
/// <param name="PointerDelta">Pointer movement since the previous snapshot.</param>
/// <param name="WheelDelta">Wheel movement since the previous snapshot.</param>
/// <param name="ControlSize">The input surface's control size (for the owned app's own mapping).</param>
/// <param name="NormalizedPointer">The pointer mapped into the displayed content image (0..1 per axis,
/// top-left origin), for consumers that render content the control merely displays.</param>
public sealed record InputSnapshot(
    bool Focused,
    IReadOnlyList<Key> Keys,
    IReadOnlyList<MouseButton> Buttons,
    Point PointerPosition,
    Vector PointerDelta,
    double WheelDelta,
    Size ControlSize,
    Point NormalizedPointer = default);
