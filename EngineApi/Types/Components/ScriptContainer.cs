using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Components;

/// <summary>The script asset bindings attached to an entity, mirroring the engine's
/// <c>ScriptContainer</c>.</summary>
public sealed partial class ScriptContainer : Component
{
    public ScriptContainer() => Scripts = [];

    /// <summary>The bound scripts; one value — assign a new list to edit.</summary>
    [EngineSync(Converter = typeof(ScriptContainerBindingListConverter))]
    public partial IReadOnlyList<ScriptContainerBinding> Scripts { get; set; }
}
