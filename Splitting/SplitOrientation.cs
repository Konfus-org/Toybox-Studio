namespace Toybox.Studio.Splitting;

/// <summary>
/// How a <see cref="SplitBranch"/> arranges its two children. <see cref="Horizontal"/> lays them out
/// side by side (a vertical divider between them); <see cref="Vertical"/> stacks them (a horizontal
/// divider). New members append — the value persists numerically inside saved layouts.
/// </summary>
public enum SplitOrientation
{
    Horizontal,
    Vertical,
}
