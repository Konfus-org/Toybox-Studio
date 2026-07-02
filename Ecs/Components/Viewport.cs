using System.Numerics;

namespace Toybox.Studio.Ecs.Components;

/// <summary>Serialized mirror of the engine's <c>Viewport</c> — the sub-rect a camera renders into (offset +
/// size). A plain value type nested in <see cref="Camera"/>.</summary>
public sealed class Viewport
{
    public Vector2 Position { get; set; }

    public Size Dimensions { get; set; } = new();
}
