using System.Numerics;

namespace Toybox.Studio.Ecs;

/// <summary>The sub-rectangle of a render target a camera draws into, mirroring the engine's
/// <c>Viewport</c>. A record value: edit by assigning a changed copy back to the camera.</summary>
public sealed record Viewport
{
    public Vector2 Position { get; init; }

    public Size Dimensions { get; init; }

    public bool IsZero =>
        Position == Vector2.Zero && Dimensions.Width == 0 && Dimensions.Height == 0;
}
