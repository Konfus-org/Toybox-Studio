namespace Toybox.Studio.EngineApi;

/// <summary>
/// One entity's projected screen position in a view, from the engine's <c>view.projectEntities</c> reply.
/// <see cref="U"/>/<see cref="V"/> are normalized image coordinates (0..1, top-left origin) — the caller
/// maps them through the viewport's cover-scaling to control pixels. <see cref="Depth"/> is the entity's
/// world-space distance from the camera, used to scale a node by distance (further = smaller). Entities
/// behind the camera are omitted from the reply.
/// </summary>
public readonly record struct EntityScreen(ulong Id, double U, double V, double Depth);
