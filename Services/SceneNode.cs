namespace Toybox.Studio.Services;

/// <summary>One component on a scene entity, with its reflected data pretty-printed as JSON.</summary>
public sealed record SceneComponent(string Name, string Json);

/// <summary>UI-ready snapshot of one entity in the scene hierarchy.</summary>
public sealed class SceneNode
{
    public required ulong Id { get; init; }

    public required string Name { get; init; }

    public required string Tag { get; init; }

    public required IReadOnlyList<SceneComponent> Components { get; init; }

    public List<SceneNode> Children { get; } = [];
}
