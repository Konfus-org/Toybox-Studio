using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Ecs;

/// <summary>
/// One engine entity, mirrored: the synced properties push through <see cref="EngineCommands.EntitySet"/>
/// as they're edited, <see cref="Id"/> only ever flows in from the engine (Mirror), and the structural
/// operations are generated engine commands — their bodies are the address plus the parameters, written
/// by the sync generator.
/// </summary>
[EngineSync(Address = "entity/{Id}")]
public sealed partial class Entity
{
    public Entity() => Name = "Entity";

    /// <summary>The engine-assigned id; engine → studio only (a Mirror setter never pushes, so the
    /// entity can only assign it locally — say, from a create reply).</summary>
    [EngineSync(EngineCommands.EntitySet, SyncMode.Mirror)]
    public partial ulong Id { get; private set; }

    [EngineSync(EngineCommands.EntitySet)]
    public partial string Name { get; set; }

    [EngineSync(EngineCommands.EntitySet)]
    public partial bool IsEnabled { get; set; }

    /// <summary>The entity's sibling order; batched wide so a drag-reorder settles before pushing.</summary>
    [EngineSync(EngineCommands.EntitySet, SyncMode.Batched, batchFrequencyMs: 250)]
    public partial int Order { get; set; }

    /// <summary>Asks the engine to clone this entity (children included).</summary>
    [EngineSync(EngineCommands.EntityDuplicate)]
    public partial Task<Result> DuplicateAsync(CancellationToken ct = default);

    /// <summary>Asks the engine to destroy this entity and its children.</summary>
    [EngineSync(EngineCommands.EntityDestroy)]
    public partial Task<Result> DestroyAsync(CancellationToken ct = default);

    /// <summary>Asks the engine to attach a component by its registered name.</summary>
    [EngineSync(EngineCommands.EntityAddComponent)]
    public partial Task<Result> AddComponentAsync(string component, CancellationToken ct = default);
}
