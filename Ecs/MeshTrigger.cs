using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>A mesh-shaped overlap trigger sourced from the entity's model (its
/// <see cref="Renderer"/>), mirroring the engine's <c>MeshTrigger</c>.</summary>
public sealed partial class MeshTrigger : Trigger
{
    public MeshTrigger() => IsConvex = true;

    public MeshTrigger(bool isConvex) => IsConvex = isConvex;

    [EngineSync]
    public partial bool IsConvex { get; set; }
}
