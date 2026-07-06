using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// Runs <see cref="MotionClip"/>s on visuals — the single doorway every clip plays through. It resolves the
/// live animation intensity and skips the clip entirely at 0 (the app's reduce-motion contract), centers
/// the transform origin so rotations and scales pivot naturally, and registers the keyframe animator that
/// lets clips drive a whole <c>RenderTransform</c> as a <c>TransformOperations</c> (Avalonia 12 registers
/// none for the keyframe path — see <see cref="TransformOperationsAnimator"/>).
/// </summary>
public static class MotionPlayer
{
    static MotionPlayer()
    {
        Animation.RegisterCustomAnimator<ITransform, TransformOperationsAnimator>();
    }

    /// <summary>The live 0..1 animation intensity at this visual (0 when the resource isn't published).</summary>
    public static double IntensityOf(Visual visual) =>
        visual.TryFindResource(MotionTokens.IntensityKey, out var value) && value is double intensity ? intensity : 0;

    /// <summary>
    /// Plays one run of the clip on the target, completing when it finishes or the token cancels. An
    /// interrupted or finished <see cref="FillMode.None"/> clip reverts to the target's own transform, so
    /// steady-state style transforms (hover grows, rest scales) survive every clip. Does nothing at
    /// intensity 0.
    /// </summary>
    public static async Task PlayAsync(Visual target, MotionClip clip, CancellationToken cancellation = default)
    {
        var intensity = IntensityOf(target);
        if (intensity <= 0 || cancellation.IsCancellationRequested)
            return;

        target.RenderTransformOrigin = RelativePoint.Center;
        try
        {
            await clip.Build(intensity).RunAsync(target, cancellation);
        }
        catch (OperationCanceledException)
        {
            // An interrupted clip is normal (a state change, a teardown); the next clip takes over from rest.
        }
    }
}
