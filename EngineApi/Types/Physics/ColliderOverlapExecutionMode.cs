using Toybox.Studio.EngineApi.Types.Components;
using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Physics;

/// <summary>When a trigger evaluates overlap queries, mirroring the engine's
/// <c>ColliderOverlapExecutionMode</c>.</summary>
public enum ColliderOverlapExecutionMode
{
    Auto = 0,
    Manual = 1,
}
