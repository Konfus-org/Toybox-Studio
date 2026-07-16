using Toybox.Studio.EngineApi.Types.Components;
using Toybox.Studio.EngineApi.Types.Worlds;
using Icon = IconPacks.Avalonia.Lucide.PackIconLucideKind;

namespace Toybox.Studio.NodeGraph;

/// <summary>
/// Picks a Lucide icon for an entity node and its component rows from the component type — the engine's
/// component mirrors carry no icon attribute, so this is the one place the mapping lives. An entity's icon
/// is the icon of its most characterizing component (camera, light, …), falling back to a generic box.
/// </summary>
public static class NodeIcons
{
    // The component that most characterizes an entity, most-specific first.
    private static readonly string[] Priority =
        ["Camera", "Light", "Sky", "Particles", "PostProcessing", "Renderer", "ScriptContainer"];

    public static Icon ForEntity(Entity entity)
    {
        foreach (var kind in Priority)
            if (entity.Components.FirstOrDefault(component => component.GetType().Name == kind) is { } match)
                return ForComponent(match);
        return Icon.Box;
    }

    public static Icon ForComponent(Component component) => component.GetType().Name switch
    {
        "Camera" => Icon.Camera,
        "Light" => Icon.Lightbulb,
        "Sky" => Icon.Cloud,
        "Particles" => Icon.Sparkles,
        "PostProcessing" => Icon.Palette,
        "Renderer" => Icon.Box,
        "ScriptContainer" => Icon.Code,
        "Transform" => Icon.Move,
        "Lods" => Icon.Layers,
        var name when name.Contains("Collider") || name.Contains("Trigger") => Icon.Shield,
        _ => Icon.Box,
    };
}
