namespace Toybox.Studio.Assets;

/// <summary>A float-producing mouse control (the wheel), mirroring the engine's
/// <c>MouseAxisInputControl</c>.</summary>
public sealed record MouseAxisInputControl : InputControl
{
    public InputMouseAxisControl Control { get; init; } = InputMouseAxisControl.Wheel;
}
