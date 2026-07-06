using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>The script asset bindings attached to an entity, mirroring the engine's
/// <c>ScriptContainer</c>.</summary>
public sealed partial class ScriptContainer : Component
{
    public ScriptContainer() => Scripts = [];

    /// <summary>The bound scripts; one value — assign a new list to edit.</summary>
    [EngineSync(Converter = typeof(ScriptContainerBindingListConverter))]
    public partial IReadOnlyList<ScriptContainerBinding> Scripts { get; set; }
}
