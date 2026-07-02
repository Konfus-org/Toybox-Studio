using System.Collections.Generic;
using System.Numerics;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Project.Assets;

/// <summary>
/// The strongly-typed payload of a <c>.chunk</c> asset (the data behind an <c>Asset&lt;Chunk&gt;</c>): one streamed
/// region of a world, addressed by its <see cref="Coord"/> (logical chunk coordinate) and holding the
/// <see cref="Entities"/> that stream in with it. Mirrors the engine's serialized chunk so a chunk is a normal
/// asset — inspectable and round-trippable like any other.
/// </summary>
[AssetInfo("chunk")]
public sealed partial class Chunk : AssetData
{
    [EngineSync] private Vector3 _coord;

    [EngineSync] private IReadOnlyList<EntityData> _entities = [];
}
