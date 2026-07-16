using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>A button on a target controller, mirroring the engine's
/// <c>ControllerButtonInputControl</c>.</summary>
public sealed record ControllerButtonInputControl : InputControl
{
    public int ControllerIndex { get; init; } = -1;

    public InputControllerButton Button { get; init; } = InputControllerButton.Unknown;
}
