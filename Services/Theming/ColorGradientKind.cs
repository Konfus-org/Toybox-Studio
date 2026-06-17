namespace Toybox.Studio.Services.Theming;

/// <summary>
/// How a <see cref="ColorGradient"/>'s two stops are laid out across a surface.
/// </summary>
public enum ColorGradientKind
{
    /// <summary>An angled straight ramp from start to end.</summary>
    Linear,

    /// <summary>A soft pool of the start colour in the upper-left, fading to the end toward the lower-right.</summary>
    Radial,
}
