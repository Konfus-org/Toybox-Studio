namespace Toybox.Studio.Assets;

/// <summary>A button on a target controller, mirroring the engine's
/// <c>ControllerButtonInputControl</c>.</summary>
public sealed record ControllerButtonInputControl : InputControl
{
    public int ControllerIndex { get; init; } = -1;

    public InputControllerButton Button { get; init; } = InputControllerButton.Unknown;
}
