using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Components;

/// <summary>
/// One behavior attached to an <see cref="Entity"/>, mirrored from the engine. The class-level
/// [EngineSync] does all the rooting: it sets the family default (component edits push through the
/// world-qualified <see cref="EngineCommands.SyncSet"/> path), declares the wire address every payload
/// carries, and opts the type into the injected sync base — neither this class nor any concrete component
/// names it. The component addresses itself relative to its owning entity
/// (<c>{EntityAddress}/components/{Name}</c>, i.e. <c>world/{w}/entities/{id}/components/{name}</c>): it
/// points to its entity, and the entity to its world — no world id is duplicated here.
/// </summary>
[EngineSync(EngineCommands.SyncSet, Address = "{EntityAddress}/components/{Name}")]
public abstract partial class Component
{
    protected Component() => Name = string.Empty;

    /// <summary>The owning entity's engine id. The owning <see cref="Entity"/> stamps it on each component
    /// before it binds (see <see cref="EngineObject.PrepareChild"/>).</summary>
    public ulong EntityId { get; internal set; }

    /// <summary>The owning entity's world-qualified address (<c>world/{w}/entities/{id}</c>) — the prefix
    /// this component's own address extends. The owning <see cref="Entity"/> stamps its address here before
    /// the component binds, so the component's world routing follows its entity's; routing only, not
    /// synced.</summary>
    internal EngineAddress EntityAddress { get; set; }

    /// <summary>The component's engine-registered name (<c>transform</c>, <c>box_collider</c>, …), synced
    /// like every other property.</summary>
    [EngineSync]
    public partial string Name { get; set; }
}
