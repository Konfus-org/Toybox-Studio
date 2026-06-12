namespace Toybox.Studio.Services;

/// <summary>
/// The two base variants a theme can target. Named <c>ThemeMode</c> rather than <c>ThemeVariant</c> to
/// avoid colliding with <see cref="Avalonia.Styling.ThemeVariant"/>, which <see cref="ThemeManager"/>
/// also uses.
/// </summary>
public enum ThemeMode
{
    Dark,
    Light,
}
