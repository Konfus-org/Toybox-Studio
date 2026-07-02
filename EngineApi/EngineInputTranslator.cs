using Avalonia.Input;
using Toybox.Studio.Input;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// Strongly-typed keyboard key identifiers forwarded to the engine input system. Mirrors the engine's
/// <c>tbx::InputKey</c> (see <c>tbx/systems/input/action.h</c>) value-for-value, so a forwarded key
/// matches what the engine reports for the physical key. The numeric values are USB HID usage ids
/// (the engine's canonical key codes); Studio sends the enum, never a hand-rolled SDL scancode.
/// </summary>
public enum InputKey
{
    Unknown = 0,
    A = 4,
    B = 5,
    C = 6,
    D = 7,
    E = 8,
    F = 9,
    G = 10,
    H = 11,
    I = 12,
    J = 13,
    K = 14,
    L = 15,
    M = 16,
    N = 17,
    O = 18,
    P = 19,
    Q = 20,
    R = 21,
    S = 22,
    T = 23,
    U = 24,
    V = 25,
    W = 26,
    X = 27,
    Y = 28,
    Z = 29,
    Alpha1 = 30,
    Alpha2 = 31,
    Alpha3 = 32,
    Alpha4 = 33,
    Alpha5 = 34,
    Alpha6 = 35,
    Alpha7 = 36,
    Alpha8 = 37,
    Alpha9 = 38,
    Alpha0 = 39,
    Return = 40,
    Escape = 41,
    Backspace = 42,
    Tab = 43,
    Space = 44,
    Minus = 45,
    Equals = 46,
    LeftBracket = 47,
    RightBracket = 48,
    Backslash = 49,
    NonUsHash = 50,
    Semicolon = 51,
    Apostrophe = 52,
    Grave = 53,
    Comma = 54,
    Period = 55,
    Slash = 56,
    CapsLock = 57,
    F1 = 58,
    F2 = 59,
    F3 = 60,
    F4 = 61,
    F5 = 62,
    F6 = 63,
    F7 = 64,
    F8 = 65,
    F9 = 66,
    F10 = 67,
    F11 = 68,
    F12 = 69,
    PrintScreen = 70,
    ScrollLock = 71,
    Pause = 72,
    Insert = 73,
    Home = 74,
    PageUp = 75,
    Delete = 76,
    End = 77,
    PageDown = 78,
    Right = 79,
    Left = 80,
    Down = 81,
    Up = 82,
    NumLockClear = 83,
    KpDivide = 84,
    KpMultiply = 85,
    KpMinus = 86,
    KpPlus = 87,
    KpEnter = 88,
    Kp1 = 89,
    Kp2 = 90,
    Kp3 = 91,
    Kp4 = 92,
    Kp5 = 93,
    Kp6 = 94,
    Kp7 = 95,
    Kp8 = 96,
    Kp9 = 97,
    Kp0 = 98,
    KpPeriod = 99,
    NonUsBackslash = 100,
    Application = 101,
    Power = 102,
    KpEquals = 103,
    F13 = 104,
    F14 = 105,
    F15 = 106,
    F16 = 107,
    F17 = 108,
    F18 = 109,
    F19 = 110,
    F20 = 111,
    F21 = 112,
    F22 = 113,
    F23 = 114,
    F24 = 115,
    Execute = 116,
    Help = 117,
    Menu = 118,
    Select = 119,
    Stop = 120,
    Again = 121,
    Undo = 122,
    Cut = 123,
    Copy = 124,
    Paste = 125,
    Find = 126,
    Mute = 127,
    VolumeUp = 128,
    VolumeDown = 129,
    LockingCapsLock = 130,
    LockingNumLock = 131,
    LockingScrollLock = 132,
    KpComma = 133,
    KpEqualSas400 = 134,
    International1 = 135,
    International2 = 136,
    International3 = 137,
    International4 = 138,
    International5 = 139,
    International6 = 140,
    International7 = 141,
    International8 = 142,
    International9 = 143,
    Lang1 = 144,
    Lang2 = 145,
    Lang3 = 146,
    Lang4 = 147,
    Lang5 = 148,
    Lang6 = 149,
    Lang7 = 150,
    Lang8 = 151,
    Lang9 = 152,
    AltErase = 153,
    SysReq = 154,
    Cancel = 155,
    Clear = 156,
    Prior = 157,
    Return2 = 158,
    Separator = 159,
    Out = 160,
    Oper = 161,
    ClearAgain = 162,
    CrSel = 163,
    ExSel = 164,
    Kp00 = 176,
    Kp000 = 177,
    ThousandsSeparator = 178,
    DecimalSeparator = 179,
    CurrencyUnit = 180,
    CurrencySubUnit = 181,
    KpLeftParen = 182,
    KpRightParen = 183,
    KpLeftBrace = 184,
    KpRightBrace = 185,
    KpTab = 186,
    KpBackspace = 187,
    KpA = 188,
    KpB = 189,
    KpC = 190,
    KpD = 191,
    KpE = 192,
    KpF = 193,
    KpXor = 194,
    KpPower = 195,
    KpPercent = 196,
    KpLess = 197,
    KpGreater = 198,
    KpAmpersand = 199,
    KpDblAmpersand = 200,
    KpVerticalBar = 201,
    KpDblVerticalBar = 202,
    KpColon = 203,
    KpHash = 204,
    KpSpace = 205,
    KpAt = 206,
    KpExclam = 207,
    KpMemStore = 208,
    KpMemRecall = 209,
    KpMemClear = 210,
    KpMemAdd = 211,
    KpMemSubtract = 212,
    KpMemMultiply = 213,
    KpMemDivide = 214,
    KpPlusMinus = 215,
    KpClear = 216,
    KpClearEntry = 217,
    KpBinary = 218,
    KpOctal = 219,
    KpDecimal = 220,
    KpHexadecimal = 221,
    LCtrl = 224,
    LShift = 225,
    LAlt = 226,
    LGui = 227,
    RCtrl = 228,
    RShift = 229,
    RAlt = 230,
    RGui = 231,
    Mode = 257,
    Sleep = 258,
    Wake = 259,
    ChannelIncrement = 260,
    ChannelDecrement = 261,
    MediaPlay = 262,
    MediaPause = 263,
    MediaRecord = 264,
    MediaFastForward = 265,
    MediaRewind = 266,
    MediaNextTrack = 267,
    MediaPreviousTrack = 268,
    MediaStop = 269,
    MediaEject = 270,
    MediaPlayPause = 271,
    MediaSelect = 272,
    AcNew = 273,
    AcOpen = 274,
    AcClose = 275,
    AcExit = 276,
    AcSave = 277,
    AcPrint = 278,
    AcProperties = 279,
    AcSearch = 280,
    AcHome = 281,
    AcBack = 282,
    AcForward = 283,
    AcStop = 284,
    AcRefresh = 285,
    AcBookmarks = 286,
    SoftLeft = 287,
    SoftRight = 288,
    Call = 289,
    EndCall = 290,
    Reserved = 400,
    Count = 512,
}

/// <summary>
/// One typed input snapshot in the engine's input wire format — the output of
/// <see cref="EngineInputTranslator.Translate"/>. Buttons: bit0 left, bit1 right, bit2 middle.
/// MoveKeys (the fly camera): bit0 fwd, 1 back, 2 left, 3 right, 4 up, 5 down.
/// </summary>
public readonly record struct EngineInput(int Buttons, int MoveKeys, IReadOnlyList<InputKey> Keys);

/// <summary>
/// The one place the UI framework's typed input becomes the engine's input wire format: held
/// <see cref="Key"/>s become engine <see cref="InputKey"/> codes (and the fly-camera move-key
/// bitmask), held <see cref="MouseButton"/>s become the button bitmask. Used by
/// <see cref="Engine.StreamInput"/>; nothing outside the engine layer knows this vocabulary.
/// </summary>
public static class EngineInputTranslator
{
    /// <summary>Translates a captured snapshot's keys and buttons to the engine's wire format.</summary>
    public static EngineInput Translate(InputSnapshot input) =>
        new(ButtonBits(input.Buttons), MoveKeyBits(input.Keys), WireKeys(input.Keys));

    // Maps an Avalonia key to the engine's InputKey code, so forwarded game input matches what the
    // engine's input manager reports for the physical key. Covers the keys games commonly read;
    // unmapped keys return Unknown and are dropped.
    private static InputKey Map(Key key) => key switch
    {
        // Letters: InputKey.A … InputKey.Z are contiguous, like Avalonia Key.A … Key.Z.
        >= Key.A and <= Key.Z => InputKey.A + (key - Key.A),

        // Digit row: InputKey.Alpha1 … Alpha9 are contiguous; 0 sorts after 9.
        Key.D0 => InputKey.Alpha0,
        >= Key.D1 and <= Key.D9 =>
            InputKey.Alpha1 + (key - Key.D1),

        // Function keys: InputKey.F1 … F12 are contiguous.
        >= Key.F1 and <= Key.F12 =>
            InputKey.F1 + (key - Key.F1),

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

    // The held keys as engine InputKey codes (unmapped keys dropped).
    private static List<InputKey> WireKeys(IReadOnlyList<Key> keys)
    {
        var wire = new List<InputKey>(keys.Count);
        foreach (var key in keys)
        {
            var mapped = Map(key);
            if (mapped != InputKey.Unknown)
                wire.Add(mapped);
        }

        return wire;
    }

    // Held buttons as the wire bitmask.
    private static int ButtonBits(IReadOnlyList<MouseButton> buttons)
    {
        var bits = 0;
        foreach (var button in buttons)
        {
            bits |= button switch
            {
                MouseButton.Left => 0x1,
                MouseButton.Right => 0x2,
                MouseButton.Middle => 0x4,
                _ => 0,
            };
        }

        return bits;
    }

    // Held fly-camera movement keys as the wire bitmask.
    private static int MoveKeyBits(IReadOnlyList<Key> keys)
    {
        var bits = 0;
        foreach (var key in keys)
        {
            bits |= key switch
            {
                Key.W => 0x01,
                Key.S => 0x02,
                Key.A => 0x04,
                Key.D => 0x08,
                Key.E => 0x10,
                Key.Q => 0x20,
                _ => 0,
            };
        }

        return bits;
    }
}
