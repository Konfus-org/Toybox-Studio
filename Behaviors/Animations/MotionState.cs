namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// One named state in a <see cref="MotionStateBehavior"/> machine: what plays once on the way in
/// (<see cref="Enter"/>), what idles while the state holds (<see cref="Loop"/>), and an occasional
/// one-shot <see cref="Flourish"/> spliced between loop cycles — never mid-cycle, so motion always hands
/// off from the rest pose. Declared in XAML inside a <see cref="MotionStates"/> collection; every clip is
/// optional (an enter-only state nods and rests; a nameless match stops all motion).
/// </summary>
public sealed class MotionState
{
    /// <summary>The state's name, matched against the bound state value's <c>ToString</c>.</summary>
    public string Name { get; set; } = "";

    /// <summary>Played once on entering the state, before the loop starts.</summary>
    public MotionClip? Enter { get; set; }

    /// <summary>Replayed cycle after cycle while the state is active.</summary>
    public MotionClip? Loop { get; set; }

    /// <summary>An occasional one-shot spliced between loop cycles.</summary>
    public MotionClip? Flourish { get; set; }

    /// <summary>The soonest a flourish may follow the previous one (or the state entry), in seconds.</summary>
    public double FlourishMinSeconds { get; set; } = 6;

    /// <summary>The latest a flourish waits before playing, in seconds.</summary>
    public double FlourishMaxSeconds { get; set; } = 12;
}
