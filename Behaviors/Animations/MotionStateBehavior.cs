using Avalonia;
using Avalonia.Controls;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// A small animation state machine for a control: declare named <see cref="MotionState"/>s in XAML (each an
/// optional enter clip, idle loop and occasional flourish) and bind <see cref="StateProperty"/> to a
/// view-model value; whichever state the value's <c>ToString</c> names plays. The splash icon uses it —
/// rocking while the editor loads with the occasional full spin, a nod once ready — and any control wanting
/// per-state idle motion attaches the same way. A state change interrupts the current motion and plays the
/// new state's enter clip before settling into its loop; a name matching no state simply stops all motion.
/// Everything runs through <see cref="MotionPlayer"/>, so intensity 0 keeps the control perfectly still.
/// </summary>
public static class MotionStateBehavior
{
    public static readonly AttachedProperty<MotionStates?> StatesProperty =
        AvaloniaProperty.RegisterAttached<Control, MotionStates?>("States", typeof(MotionStateBehavior));

    public static readonly AttachedProperty<object?> StateProperty =
        AvaloniaProperty.RegisterAttached<Control, object?>("State", typeof(MotionStateBehavior));

    // The per-control machine, created on the first States/State assignment and torn down with the control.
    private static readonly AttachedProperty<Machine?> MachineProperty =
        AvaloniaProperty.RegisterAttached<Control, Machine?>("Machine", typeof(MotionStateBehavior));

    static MotionStateBehavior()
    {
        StatesProperty.Changed.AddClassHandler<Control>((control, _) => Apply(control));
        StateProperty.Changed.AddClassHandler<Control>((control, _) => Apply(control));
    }

    public static void SetStates(Control control, MotionStates? value) => control.SetValue(StatesProperty, value);
    public static MotionStates? GetStates(Control control) => control.GetValue(StatesProperty);
    public static void SetState(Control control, object? value) => control.SetValue(StateProperty, value);
    public static object? GetState(Control control) => control.GetValue(StateProperty);

    // States and State arrive in either order (attached-property sets vs. bindings); (re)evaluate on both.
    private static void Apply(Control control)
    {
        if (control.GetValue(StatesProperty) is not { } states)
            return;

        var machine = control.GetValue(MachineProperty);
        if (machine is null)
        {
            machine = new Machine(control);
            control.SetValue(MachineProperty, machine);
        }

        var name = control.GetValue(StateProperty)?.ToString();
        machine.TransitionTo(states.FirstOrDefault(state => state.Name == name));
    }

    /// <summary>
    /// The per-control runtime: one active state, one cancellation source. The loop replays whole cycles
    /// and splices flourishes between them, so every hand-off happens from the rest pose (no mid-cycle
    /// snapping); a state change cancels whatever is in flight and the interrupted clip reverts.
    /// </summary>
    private sealed class Machine
    {
        // How often to re-check for a raised intensity while parked at reduce-motion 0.
        private static readonly TimeSpan ReducedMotionPoll = TimeSpan.FromMilliseconds(500);

        private readonly Control _target;
        private CancellationTokenSource? _running;
        private MotionState? _pending; // A state requested before the control loaded; it plays on Loaded.

        public Machine(Control target)
        {
            _target = target;
            // The initial state usually lands with the DataContext, before the control can render motion.
            target.Loaded += (_, _) =>
            {
                if (_pending is { } pending)
                {
                    _pending = null;
                    TransitionTo(pending);
                }
            };
            target.DetachedFromVisualTree += (_, _) => Stop();
        }

        public void TransitionTo(MotionState? state)
        {
            Stop();
            if (state is null)
                return;

            if (!_target.IsLoaded)
            {
                _pending = state;
                return;
            }

            _running = new CancellationTokenSource();
            _ = RunAsync(state, _running.Token);
        }

        private void Stop()
        {
            _pending = null;
            _running?.Cancel();
            _running = null;
        }

        private async Task RunAsync(MotionState state, CancellationToken cancellation)
        {
            try
            {
                if (state.Enter is { } enter)
                    await MotionPlayer.PlayAsync(_target, enter, cancellation);

                if (state.Loop is null)
                    return;

                var nextFlourish = NextFlourish(state);
                while (!cancellation.IsCancellationRequested)
                {
                    // At intensity 0 the player returns instantly — park instead of spinning the loop hot.
                    if (MotionPlayer.IntensityOf(_target) <= 0)
                    {
                        await Task.Delay(ReducedMotionPoll, cancellation);
                        continue;
                    }

                    await MotionPlayer.PlayAsync(_target, state.Loop, cancellation);

                    if (state.Flourish is { } flourish && DateTime.UtcNow >= nextFlourish)
                    {
                        await MotionPlayer.PlayAsync(_target, flourish, cancellation);
                        nextFlourish = NextFlourish(state);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The state changed or the control tore down; the next state's run takes over.
            }
        }

        private static DateTime NextFlourish(MotionState state)
        {
            var window = Math.Max(0, state.FlourishMaxSeconds - state.FlourishMinSeconds);
            var seconds = state.FlourishMinSeconds + Random.Shared.NextDouble() * window;
            return DateTime.UtcNow + TimeSpan.FromSeconds(seconds);
        }
    }
}
