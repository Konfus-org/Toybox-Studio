namespace Toybox.Studio.Assets;

/// <summary>A vector-producing mouse control (position or delta), mirroring the engine's
/// <c>MouseVectorInputControl</c>.</summary>
public sealed record MouseVectorInputControl : InputControl
{
    public InputMouseVectorControl Control { get; init; } = InputMouseVectorControl.Delta;
}
