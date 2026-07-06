namespace Toybox.Studio.Ecs;

/// <summary>A width/height pair, mirroring the engine's <c>Size</c>.</summary>
public readonly record struct Size(int Width, int Height)
{
    /// <summary>The aspect ratio for projection math; zero when the height is zero.</summary>
    public float AspectRatio => Height == 0 ? 0f : (float)Width / Height;
}
