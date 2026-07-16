using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The vector-producing mouse controls, mirroring the engine's
/// <c>InputMouseVectorControl</c> value-for-value.</summary>
public enum InputMouseVectorControl
{
    Position = 0,
    Delta = 1,
}
