namespace Toybox.Studio.Settings;

/// <summary>
/// The accent colour a browser category tints its assets' icons with. A small named palette the settings
/// grid offers as a dropdown; the browser maps each to a concrete colour. <see cref="Default"/> is the
/// untinted, theme-neutral choice.
/// </summary>
public enum CategoryColor
{
    Default,
    Gray,
    Red,
    Orange,
    Amber,
    Yellow,
    Green,
    Teal,
    Cyan,
    Blue,
    Indigo,
    Violet,
    Magenta,
    Pink,
}
