using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>A solid mesh collision shape sourced from the entity's model (its
/// <see cref="Renderer"/>), mirroring the engine's <c>MeshCollider</c>.</summary>
public sealed partial class MeshCollider : Collider
{
    public MeshCollider() => IsConvex = true;

    public MeshCollider(bool isConvex) => IsConvex = isConvex;

    [EngineSync]
    public partial bool IsConvex { get; set; }
}
