using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Assets;

/// <summary>Four directional keys composed into one Vector2 action value (WASD), mirroring the
/// engine's <c>KeyboardVector2CompositeInputControl</c>.</summary>
public sealed record KeyboardVector2CompositeInputControl : InputControl
{
    public InputKey Up { get; init; } = InputKey.Unknown;

    public InputKey Down { get; init; } = InputKey.Unknown;

    public InputKey Left { get; init; } = InputKey.Unknown;

    public InputKey Right { get; init; } = InputKey.Unknown;
}
