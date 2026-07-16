using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Components;

/// <summary>
/// Renders a model asset for an entity, with optional per-slot material overrides — the only
/// renderable component. <see cref="Materials"/> overrides the model's per-slot default material
/// instances: entry <c>i</c> overrides slot <c>i</c>, an empty handle inherits the model's own slot,
/// and the list may be shorter than the model's slot count.
/// </summary>
public sealed partial class Renderer : Component
{
    public Renderer() => Materials = [];

    public Renderer(Handle model)
        : this()
        => Model = model;

    /// <summary>The model asset providing mesh geometry and default material slots.</summary>
    [EngineSync]
    public partial Handle Model { get; set; }

    /// <summary>Per-slot material assignments (<c>.mti</c>/<c>.mat</c> handles), aligned 1:1 with the
    /// model's material slots. The list is one value — assign a new list to edit.</summary>
    [EngineSync(Converter = typeof(HandleListConverter))]
    public partial IReadOnlyList<Handle> Materials { get; set; }
}
