using Toybox.Studio.Assets;

namespace Toybox.Studio.Ecs;

/// <summary>
/// One material-driven post-processing step in a <see cref="PostProcessing"/> stack, mirroring the
/// engine's <c>PostProcessingEffect</c> (its runtime gameplay-tag gate never serializes, so it is not
/// mirrored). A record value inside the effect list — edit by assigning an updated list back to the
/// component.
/// </summary>
public sealed record PostProcessingEffect
{
    /// <summary>The material instance shading this fullscreen pass.</summary>
    public MaterialInstance Material { get; init; } = new();

    /// <summary>Enables or disables this effect without removing it from the stack.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>The blend weight between source scene color (0) and effect output (1).</summary>
    public float Blend { get; init; } = 1.0f;
}
