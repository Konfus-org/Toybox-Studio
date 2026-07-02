namespace Toybox.Studio.Ecs.Components;

/// <summary>
/// Serialized mirror of the engine's <c>RenderTarget</c> — the surface a camera renders into. The native window
/// handle is a runtime pointer the engine owns (not serialized), so only the size is modeled. Engine-managed, so
/// it's shown read-only.
/// </summary>
public sealed class RenderTarget
{
    public Size Size { get; set; } = new();
}
