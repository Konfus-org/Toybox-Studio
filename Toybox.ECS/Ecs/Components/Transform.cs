using System.Numerics;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Ecs.Components;

/// <summary>
/// Typed, engine-synced view of the <c>transform</c> component: local-space position, rotation and scale. Loaded
/// bound to its entity via <see cref="Entity.GetComponentAsync{T}"/>, so setting a property pushes the
/// change live through <c>sync.set</c>. The engine component name (<c>"transform"</c>) is derived from the
/// class name; the public properties, the <c>ApplyField</c> inbound router, and the wire manifest are emitted by
/// the EngineSync generator from the <see cref="EngineSyncAttribute"/> fields below.
/// </summary>
[IconAttribute(Icon.Move3d, PaletteColor.Blue)]
public sealed partial class Transform : Component
{
    [EngineSync] private Vector3 _position;

    [EngineSync] private Quaternion _rotation = Quaternion.Identity;

    [EngineSync] private Vector3 _scale = Vector3.One;
}
