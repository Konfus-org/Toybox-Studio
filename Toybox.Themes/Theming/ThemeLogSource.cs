using Toybox.Studio.Logging;

namespace Toybox.Studio.Theming;

/// <summary>
/// Adapts the editor <see cref="ThemeManager"/> to the logging layer's <see cref="ILogTheme"/>, so the
/// core <see cref="Logger"/> can push the theme's log colours to the engine console without referencing
/// the theming system. Collapses each (usually flat) semantic colour to its representative hex.
/// </summary>
public sealed class ThemeLogSource : ILogTheme
{
    private readonly ThemeManager _theme;

    public ThemeLogSource(ThemeManager theme)
    {
        _theme = theme;
        _theme.ThemeChanged += () => Changed?.Invoke();
    }

    public event Action? Changed;

    public (string Info, string Warning, string Error) Colors
    {
        get
        {
            var colors = _theme.Active.Colors;
            return (
                ColorJsonConverter.ToHex(colors.Info.Representative),
                ColorJsonConverter.ToHex(colors.Warning.Representative),
                ColorJsonConverter.ToHex(colors.Error.Representative));
        }
    }
}
