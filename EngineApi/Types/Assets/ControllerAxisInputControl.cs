using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>An axis on a target controller, mirroring the engine's
/// <c>ControllerAxisInputControl</c>.</summary>
public sealed record ControllerAxisInputControl : InputControl
{
    public int ControllerIndex { get; init; } = -1;

    public InputControllerAxis Axis { get; init; } = InputControllerAxis.Unknown;
}
