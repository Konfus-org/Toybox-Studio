namespace Toybox.Studio.NodeGraph;

/// <summary>
/// One reference held by an entity — a plug on its node. <see cref="Label"/> names where it came from
/// (e.g. <c>Parent</c> or <c>Renderer.model</c>); <see cref="Kind"/> decides rendering; when the target is
/// another entity, <see cref="TargetEntityId"/> is that entity's id (0 otherwise) so the overlay can draw a
/// link to its node.
/// </summary>
public sealed record NodeReference(string Label, ReferenceKind Kind, ulong TargetEntityId);
