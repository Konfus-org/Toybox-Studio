using System;

namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// The Stretch=UniformToFill (cover / centre-crop) mapping shared by the surface display
/// (<see cref="CompositionInteropViewport"/>) and viewport picking: the engine's shared texture is scaled to
/// fill the control while preserving its aspect ratio (centred, overflowing and clipped on the long axis), so
/// a narrow or short panel never squishes the image and there are never letterbox/pillarbox bars. Both the
/// displayed image rect and the rect a click is normalized against come from <see cref="ImageRect"/>, so what
/// is shown and what is picked can never drift apart.
/// </summary>
internal static class ViewportMapping
{
    /// <summary>
    /// The destination rect (offset + size) the surface occupies inside the control: the smallest centred rect
    /// that covers the control while preserving the surface's aspect ratio, overflowing the control on the long
    /// axis (the caller clips). Falls back to the whole control when a size is degenerate.
    /// </summary>
    public static (double X, double Y, double Width, double Height) ImageRect(
        double controlW, double controlH, double surfW, double surfH)
    {
        if (controlW <= 0 || controlH <= 0 || surfW <= 0 || surfH <= 0)
            return (0, 0, controlW, controlH);

        var scale = Math.Max(controlW / surfW, controlH / surfH);
        var width = surfW * scale;
        var height = surfH * scale;
        return ((controlW - width) / 2, (controlH - height) / 2, width, height);
    }

    /// <summary>
    /// Maps a control-space point to normalized image coordinates in [0,1] (origin top-left, matching the
    /// pointer). Returns false when the sizes are degenerate. With cover scaling the image fills the whole
    /// control, so any in-bounds point maps inside [0,1]; the bounds check still guards points past the edge.
    /// </summary>
    public static bool TryNormalize(
        double px, double py, double controlW, double controlH, double surfW, double surfH,
        out double u, out double v)
    {
        u = 0;
        v = 0;
        var (x, y, width, height) = ImageRect(controlW, controlH, surfW, surfH);
        if (width <= 0 || height <= 0)
            return false;

        u = (px - x) / width;
        v = (py - y) / height;
        return u is >= 0 and <= 1 && v is >= 0 and <= 1;
    }

    /// <summary>
    /// Maps a control-space point to normalized image coordinates, clamped to [0,1] (so a point in the
    /// letterbox bars or past the control edge folds onto the nearest image edge). Used for a marquee whose
    /// corners may fall outside the image.
    /// </summary>
    public static (double U, double V) NormalizeClamped(
        double px, double py, double controlW, double controlH, double surfW, double surfH)
    {
        var (x, y, width, height) = ImageRect(controlW, controlH, surfW, surfH);
        if (width <= 0 || height <= 0)
            return (0, 0);

        return (Math.Clamp((px - x) / width, 0, 1), Math.Clamp((py - y) / height, 0, 1));
    }
}
