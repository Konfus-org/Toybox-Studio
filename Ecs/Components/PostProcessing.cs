using System.Collections.Generic;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Viewport;
using Toybox.Studio.Project;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Ecs.Components;

/// <summary>
/// Typed, engine-synced view of the <c>post_processing</c> component: the ordered stack of
/// <see cref="PostProcessingEffect"/> steps applied in the final screen pass. Mirrors the engine's serialized
/// <c>PostProcessing</c> fields.
/// </summary>
[IconAttribute(Icon.Aperture, PaletteColor.Magenta)]
[ViewportIconAttribute(Icon.Aperture, PaletteColor.Magenta)]
public sealed partial class PostProcessing : Component
{
    [EngineSync] private IReadOnlyList<PostProcessingEffect> _effects = [];
}
