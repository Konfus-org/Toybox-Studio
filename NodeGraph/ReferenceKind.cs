namespace Toybox.Studio.NodeGraph;

/// <summary>What a node plug references, which decides how it renders: an <see cref="Entity"/> reference
/// draws a link to that entity's node; an <see cref="Asset"/> or <see cref="Script"/> reference has no node
/// to link to, so its plug just lights up as "connected".</summary>
public enum ReferenceKind
{
    Entity,
    Asset,
    Script,
}
