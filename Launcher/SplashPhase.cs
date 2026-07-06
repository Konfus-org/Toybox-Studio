namespace Toybox.Studio;

/// <summary>
/// Where startup is, as the splash tells it — the motion state the splash icon plays (the names match the
/// MotionState declarations in SplashWindow.axaml).
/// </summary>
public enum SplashPhase
{
    /// <summary>Startup is underway: the icon rocks, with the occasional full spin.</summary>
    Loading,

    /// <summary>Startup finished: the icon nods once and rests.</summary>
    Ready,
}
