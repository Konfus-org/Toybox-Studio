using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>The scene-wide post-processing stack applied on the final screen pass, mirroring the
/// engine's <c>PostProcessing</c>: an ordered list of effects applied first to last.</summary>
public sealed partial class PostProcessing : Component
{
    public PostProcessing() => Effects = [];

    /// <summary>The effect stack; one value — assign a new list to edit.</summary>
    [EngineSync(Converter = typeof(PostProcessingEffectListConverter))]
    public partial IReadOnlyList<PostProcessingEffect> Effects { get; set; }
}
