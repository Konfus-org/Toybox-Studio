namespace Toybox.Studio.Ecs.Components;

/// <summary>Serialized mirror of the engine's <c>Size</c> — a width/height pair (pixels). A plain value type used
/// by <see cref="Viewport"/> and <see cref="RenderTarget"/>.</summary>
public sealed class Size
{
    public uint Width { get; set; }

    public uint Height { get; set; }
}
