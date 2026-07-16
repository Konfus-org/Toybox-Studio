using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>A vector-producing mouse control (position or delta), mirroring the engine's
/// <c>MouseVectorInputControl</c>.</summary>
public sealed record MouseVectorInputControl : InputControl
{
    public InputMouseVectorControl Control { get; init; } = InputMouseVectorControl.Delta;
}
