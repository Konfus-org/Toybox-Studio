using Avalonia.Input;

namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// Maps Avalonia <see cref="Key"/> values to SDL3 scancodes (USB HID usage ids), so forwarded game
/// input matches what the engine's SDL-backed input manager reports via <c>get_keyboard_state</c>.
/// Covers the keys games commonly read; unmapped keys return 0 and are dropped.
/// </summary>
public static class SdlScancodes
{
    public static int Map(Key key) => key switch
    {
        // Letters: SDL A=4 … Z=29, contiguous like Avalonia Key.A … Key.Z.
        >= Key.A and <= Key.Z => 4 + (key - Key.A),

        // Digit row: SDL 1=30 … 9=38, 0=39.
        Key.D0 => 39,
        >= Key.D1 and <= Key.D9 => 30 + (key - Key.D1),

        // Function keys: SDL F1=58 … F12=69.
        >= Key.F1 and <= Key.F12 => 58 + (key - Key.F1),

        Key.Enter => 40,
        Key.Escape => 41,
        Key.Back => 42,
        Key.Tab => 43,
        Key.Space => 44,

        Key.Right => 79,
        Key.Left => 80,
        Key.Down => 81,
        Key.Up => 82,

        Key.LeftCtrl => 224,
        Key.LeftShift => 225,
        Key.LeftAlt => 226,
        Key.RightCtrl => 228,
        Key.RightShift => 229,
        Key.RightAlt => 230,

        _ => 0,
    };
}
