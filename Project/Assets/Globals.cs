using System.Collections.Generic;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Project.Assets;

/// <summary>
/// The strongly-typed payload of a <c>.globals</c> asset (the data behind an <c>Asset&lt;Globals&gt;</c>): the
/// world's global (full-lifetime, non-streamed) <see cref="Entities"/>. Mirrors the engine's serialized globals so
/// it is a normal asset alongside <see cref="Chunk"/> and the world.
///
/// On disk a <c>.globals</c> is a bare top-level entity array (not a <c>{ entities: […] }</c> object); the
/// studio_bridge describe/save presents it under the <c>entities</c> wire so this single reflected field hydrates
/// and persists through the standard asset path.
/// </summary>
[AssetInfo("globals")]
public sealed partial class Globals : AssetData
{
    [EngineSync] private IReadOnlyList<EntityData> _entities = [];
}
