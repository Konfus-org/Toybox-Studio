using Avalonia.Collections;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// The XAML-declarable collection of a control's <see cref="MotionState"/>s, assigned to
/// <see cref="MotionStateBehavior.StatesProperty"/>.
/// </summary>
public sealed class MotionStates : AvaloniaList<MotionState>;
