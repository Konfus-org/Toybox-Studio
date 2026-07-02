using Toybox.Studio.Project.Assets;
using Toybox.Studio.PropertyGrid;

namespace Toybox.Studio.Ecs.Components;

/// <summary>
/// Serialized mirror of the engine's <c>PostProcessingEffect</c> — one material-driven step in the post-process
/// stack: the material instance that shades it, an enable toggle, and a blend weight. A plain value type nested in
/// <see cref="PostProcessing"/>; its <see cref="Material"/> uses the base-aware material-instance editor.
/// </summary>
public sealed class PostProcessingEffect
{
    [View("MaterialInstance")]
    public MaterialInstance Material { get; set; } = new();

    public bool IsEnabled { get; set; } = true;

    public float Blend { get; set; } = 1.0f;
}
