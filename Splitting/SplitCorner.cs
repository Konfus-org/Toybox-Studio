namespace Toybox.Studio.Splitting;

/// <summary>Which corner of a pane a split gesture grips.</summary>
public enum SplitCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>Geometry helpers for a <see cref="SplitCorner"/>.</summary>
public static class SplitCornerExtensions
{
    /// <summary>True when the corner is on the pane's left edge.</summary>
    public static bool IsLeft(this SplitCorner corner) =>
        corner is SplitCorner.TopLeft or SplitCorner.BottomLeft;

    /// <summary>True when the corner is on the pane's top edge.</summary>
    public static bool IsTop(this SplitCorner corner) =>
        corner is SplitCorner.TopLeft or SplitCorner.TopRight;

    /// <summary>The sign of the "inward" (into the pane) direction along X from this corner: +1 from a
    /// left corner, −1 from a right corner.</summary>
    public static double InwardX(this SplitCorner corner) => corner.IsLeft() ? 1 : -1;

    /// <summary>The sign of the "inward" (into the pane) direction along Y from this corner: +1 from a
    /// top corner, −1 from a bottom corner.</summary>
    public static double InwardY(this SplitCorner corner) => corner.IsTop() ? 1 : -1;
}
