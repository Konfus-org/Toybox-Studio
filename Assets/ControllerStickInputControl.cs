namespace Toybox.Studio.Assets;

/// <summary>Two axes on a target controller composed into one Vector2 value, mirroring the engine's
/// <c>ControllerStickInputControl</c>.</summary>
public sealed record ControllerStickInputControl : InputControl
{
    public int ControllerIndex { get; init; } = -1;

    public InputControllerAxis XAxis { get; init; } = InputControllerAxis.Unknown;

    public InputControllerAxis YAxis { get; init; } = InputControllerAxis.Unknown;
}
