namespace Toybox.Studio.Logging;

/// <summary>
/// The log-colour source the <see cref="Logger"/> pushes to the engine console. Inverts logging's
/// dependency on the theme system: the theming layer supplies the current info/warning/error hex colours
/// and raises <see cref="Changed"/> when they change, so core logging never references the UI theme.
/// The colours are read on the UI thread (the <see cref="Logger"/> marshals before reading).
/// </summary>
public interface ILogTheme
{
    /// <summary>Raised when the theme's log colours change, so the logger can re-push them.</summary>
    event Action? Changed;

    /// <summary>The current info/warning/error colours as engine-console hex strings.</summary>
    (string Info, string Warning, string Error) Colors { get; }
}
