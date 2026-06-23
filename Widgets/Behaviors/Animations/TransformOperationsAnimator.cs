using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace Toybox.Studio.Widgets.Behaviors.Animations;

/// <summary>
/// Lets a keyframe <see cref="Avalonia.Animation.Animation"/> drive a whole <c>RenderTransform</c> as a
/// <see cref="TransformOperations"/> (the there-and-back nod in <see cref="ExpandNod"/>). Avalonia 12's built-in
/// animator registry has no entry for an <see cref="ITransform"/>-typed property — its own
/// <c>TransformOperationsAnimator</c> is internal and only wired into the <em>transition</em> path (what
/// <see cref="Pop"/> uses), not the keyframe path — so a keyframe setter on <c>RenderTransform</c> throws
/// "No animator registered for the property RenderTransform". Registering this via
/// <see cref="Avalonia.Animation.Animation.RegisterCustomAnimator{T,TAnimator}"/> for <see cref="ITransform"/>
/// fills that gap, interpolating exactly as the engine's internal animator does (non-<see cref="TransformOperations"/>
/// endpoints fall back to <see cref="TransformOperations.Identity"/>).
/// </summary>
public sealed class TransformOperationsAnimator : InterpolatingAnimator<ITransform>
{
    public override ITransform Interpolate(double progress, ITransform oldValue, ITransform newValue)
    {
        var from = oldValue as TransformOperations ?? TransformOperations.Identity;
        var to = newValue as TransformOperations ?? TransformOperations.Identity;
        return TransformOperations.Interpolate(from, to, progress);
    }
}
