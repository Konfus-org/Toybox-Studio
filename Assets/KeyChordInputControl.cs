using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Assets;

/// <summary>
/// A keyboard key plus an exact modifier combination (Ctrl+Shift+S), mirroring the engine's
/// <c>KeyChordInputControl</c>. Unlike <see cref="KeyboardInputControl"/> the chord only activates
/// while the pressed modifiers match exactly — the shape editor shortcuts are made of.
/// </summary>
public sealed record KeyChordInputControl : InputControl
{
    public InputKey Key { get; init; } = InputKey.Unknown;

    public bool Ctrl { get; init; }

    public bool Shift { get; init; }

    public bool Alt { get; init; }

    public bool Gui { get; init; }
}
