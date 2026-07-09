namespace Toybox.Studio.Assets;

/// <summary>An axis on a target controller, mirroring the engine's
/// <c>ControllerAxisInputControl</c>.</summary>
public sealed record ControllerAxisInputControl : InputControl
{
    public int ControllerIndex { get; init; } = -1;

    public InputControllerAxis Axis { get; init; } = InputControllerAxis.Unknown;
}
