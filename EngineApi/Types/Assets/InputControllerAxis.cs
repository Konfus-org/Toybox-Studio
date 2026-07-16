using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>Gamepad axis identifiers for axis/vector input bindings, mirroring the engine's
/// <c>InputControllerAxis</c> value-for-value.</summary>
public enum InputControllerAxis
{
    Unknown = -1,
    LeftX = 0,
    LeftY = 1,
    RightX = 2,
    RightY = 3,
    LeftTrigger = 4,
    RightTrigger = 5,
}
