using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>
/// One behavior attached to an <see cref="Entity"/>, mirrored from the engine. The class-level
/// [EngineSync] does all the rooting: it sets the family default (component edits push through
/// <see cref="EngineCommands.ComponentSet"/>), declares the wire address every payload carries, and
/// opts the type into the injected sync base — neither this class nor any concrete component names it.
/// </summary>
[EngineSync(EngineCommands.ComponentSet, Address = "entity/{EntityId}/{Name}")]
public abstract partial class Component
{
    protected Component() => Name = string.Empty;

    /// <summary>The owning entity's engine id; assigned by the component's own attach path.</summary>
    public ulong EntityId { get; private set; }

    /// <summary>The component's engine-registered name (<c>Transform</c>, <c>Renderer</c>, …), synced
    /// like every other property.</summary>
    [EngineSync]
    public partial string Name { get; set; }
}
