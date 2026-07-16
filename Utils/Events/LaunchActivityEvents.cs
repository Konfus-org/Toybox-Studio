namespace Toybox.Studio.Events;

/// <summary>
/// A kind of long-running launch job the splash narrates on its secondary activity bar. Deliberately
/// coarse and phrasing-free — this low layer only names the job; the splash owns the (playful, specific)
/// caption each one gets. The producers are the domain services that run these jobs: the build runner
/// (<see cref="Compiling"/>) and the git client (<see cref="Downloading"/> a clone, <see cref="Updating"/>
/// a pull).
/// </summary>
public enum LaunchActivity
{
    /// <summary>A native project/engine compile.</summary>
    Compiling,

    /// <summary>A git clone — fetching a checkout that isn't there yet (the engine source).</summary>
    Downloading,

    /// <summary>A git pull — updating an existing checkout.</summary>
    Updating,
}

/// <summary>
/// A launch activity started (<see cref="Active"/> true) or finished (false). While one is active the
/// splash shows its activity bar captioned for the <see cref="Activity"/>; the fraction arrives separately
/// as <see cref="LaunchActivityProgress"/>. These jobs run one at a time during a launch (a git fetch,
/// then a compile), so a consumer can track just the current one. Dispatched on the job's own thread.
/// </summary>
public readonly record struct LaunchActivityChanged(LaunchActivity Activity, bool Active);

/// <summary>
/// The active launch activity reported progress: <see cref="Fraction"/> is the share done in [0,1]. Only
/// raised for jobs (and phases) whose tool reports measurable progress — a phase that reports none (a
/// cmake configure, an MSVC build, a git object count) simply raises nothing and the bar stays
/// indeterminate. Dispatched on the job's output thread.
/// </summary>
public readonly record struct LaunchActivityProgress(double Fraction);
