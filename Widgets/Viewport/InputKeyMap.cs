using Avalonia.Input;
using Toybox.Studio.Services.EngineApi;

namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// Maps Avalonia <see cref="Key"/> values to the engine's <see cref="InputKey"/> codes, so forwarded
/// game input matches what the engine's input manager reports for the physical key. Covers the keys
/// games commonly read; unmapped keys return <see cref="InputKey.Unknown"/> and are dropped.
/// </summary>
public static class InputKeyMap
{
    public static InputKey Map(Key key) => key switch
    {
        // Letters: InputKey.A … InputKey.Z are contiguous, like Avalonia Key.A … Key.Z.
        >= Key.A and <= Key.Z => InputKey.A + (key - Key.A),

        // Digit row: InputKey.Alpha1 … Alpha9 are contiguous; 0 sorts after 9.
        Key.D0 => InputKey.Alpha0,
        >= Key.D1 and <= Key.D9 => InputKey.Alpha1 + (key - Key.D1),

        // Function keys: InputKey.F1 … F12 are contiguous.
        >= Key.F1 and <= Key.F12 => InputKey.F1 + (key - Key.F1),

        Key.Enter => InputKey.Return,
        Key.Escape => InputKey.Escape,
        Key.Back => InputKey.Backspace,
        Key.Tab => InputKey.Tab,
        Key.Space => InputKey.Space,

        Key.Right => InputKey.Right,
        Key.Left => InputKey.Left,
        Key.Down => InputKey.Down,
        Key.Up => InputKey.Up,

        Key.LeftCtrl => InputKey.LCtrl,
        Key.LeftShift => InputKey.LShift,
        Key.LeftAlt => InputKey.LAlt,
        Key.RightCtrl => InputKey.RCtrl,
        Key.RightShift => InputKey.RShift,
        Key.RightAlt => InputKey.RAlt,

        _ => InputKey.Unknown,
    };
}
