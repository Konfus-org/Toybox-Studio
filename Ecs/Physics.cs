using System.Numerics;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Ecs;

/// <summary>
/// The engine's runtime physics queries, mirrored — engine-global state, so no address. One instance,
/// created and bound by the composition root; anything holding it can raycast the simulated world
/// (results are only meaningful at play time — the editing world never simulates).
/// </summary>
[EngineSync]
public sealed partial class Physics
{
    /// <summary>Casts a ray through the simulated world and returns the nearest hit, if any. Pass
    /// <paramref name="ignoreEntityId"/> to skip a body (typically the caster's own).</summary>
    [EngineSync(EngineCommands.PhysicsRaycast, Converter = typeof(RaycastHitConverter))]
    public partial Task<Result<RaycastHit>> RaycastAsync(
        Vector3 origin,
        Vector3 direction,
        float maxDistance = 100f,
        ulong ignoreEntityId = 0,
        CancellationToken ct = default);
}
