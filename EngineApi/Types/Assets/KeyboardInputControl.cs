using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>A single keyboard key, modifier-agnostic (W drives a fly-cam whether or not Shift is
/// held), mirroring the engine's <c>KeyboardInputControl</c>.</summary>
public sealed record KeyboardInputControl : InputControl
{
    public InputKey Key { get; init; } = InputKey.Unknown;
}
