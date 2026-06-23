namespace Toybox.Studio.Widgets.Toolbar;

/// <summary>
/// When a toolbar tool is shown, relative to play (game) mode. The viewport's transform tools are
/// <see cref="Any"/> (always visible); the game transport uses <see cref="Off"/> for the Play button (shown
/// only while stopped) and <see cref="On"/> for Stop and Pause/Resume (shown only while playing).
/// </summary>
public enum GameModeCondition
{
    /// <summary>Always visible, regardless of play state.</summary>
    Any,

    /// <summary>Visible only while the game is stopped (not playing).</summary>
    Off,

    /// <summary>Visible only while the game is playing.</summary>
    On,
}
