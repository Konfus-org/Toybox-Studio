namespace Toybox.Studio.Assets;

/// <summary>A single mouse button, mirroring the engine's <c>MouseButtonInputControl</c>.</summary>
public sealed record MouseButtonInputControl : InputControl
{
    public InputMouseButton Button { get; init; } = InputMouseButton.Unknown;
}
