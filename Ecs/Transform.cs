using System.Numerics;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>
/// An entity's placement in the world. Everything here is continuous drag territory, so the class-level
/// default batches: the first edit pushes at once, rapid follow-ups coalesce into one trailing push per
/// window — a gizmo drag feels live without flooding the wire.
/// </summary>
[EngineSync(EngineCommands.ComponentSet, SyncMode.Batched)]
public sealed partial class Transform : Component
{
    public Transform()
    {
        Rotation = Quaternion.Identity;
        Scale = Vector3.One;
    }

    [EngineSync]
    public partial Vector3 Position { get; set; }

    [EngineSync]
    public partial Quaternion Rotation { get; set; }

    [EngineSync]
    public partial Vector3 Scale { get; set; }
}
