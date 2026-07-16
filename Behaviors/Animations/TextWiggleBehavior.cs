using Avalonia.Controls;
using Avalonia;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// Gives a typed-into field a tiny, fast horizontal wiggle (a <see cref="WiggleClip"/>) on each keystroke —
/// tactile "juice" while typing. The wiggle moves the VISIBLE FIELD, not the text inside it: by default the
/// control that owns the inner text box (its templated parent — e.g. the NumericUpDown well) or, lacking
/// one, the text box itself; a composite control whose text box isn't a template child (the SearchBox pill)
/// points <see cref="TargetProperty"/> at the visual to wiggle instead. Only one wiggle runs per target at
/// a time (keystrokes during a wiggle are ignored), so it pulses at a steady cadence while typing and
/// settles within one short cycle once typing stops. Enabled app-wide via a Style setter on TextBox in
/// InputStyle.
/// </summary>
public static class TextWiggleBehavior
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("Enabled", typeof(TextWiggleBehavior));

    /// <summary>The visual to wiggle instead of the auto-resolved field (e.g. the SearchBox pill).</summary>
    public static readonly AttachedProperty<Control?> TargetProperty =
        AvaloniaProperty.RegisterAttached<TextBox, Control?>("Target", typeof(TextWiggleBehavior));

    private static readonly WiggleClip Wiggle = new();

    // The targets with a wiggle currently in flight — a keystroke that lands while one is running is ignored,
    // so the motion pulses at a steady cadence rather than piling up, and stops within one cycle after typing.
    private static readonly HashSet<Visual> Animating = [];

    static TextWiggleBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<TextBox>(OnEnabledChanged);
    }

    public static void SetEnabled(TextBox box, bool value) => box.SetValue(EnabledProperty, value);
    public static bool GetEnabled(TextBox box) => box.GetValue(EnabledProperty);
    public static void SetTarget(TextBox box, Control? value) => box.SetValue(TargetProperty, value);
    public static Control? GetTarget(TextBox box) => box.GetValue(TargetProperty);

    // The visible field to move: an explicit Target wins; otherwise the control whose template the text box is
    // part of (so a NumericUpDown's well wiggles, not its inner text); otherwise the text box is itself the
    // field. Shared with EnterNodBehavior so the confirmation nod lands on the same visual the wiggle does.
    internal static Visual ResolveTarget(TextBox box) =>
        GetTarget(box) ?? box.TemplatedParent as Control ?? box;

    private static void OnEnabledChanged(TextBox box, AvaloniaPropertyChangedEventArgs args)
    {
        // Detach first so the handler is wired exactly once regardless of how the flag toggles.
        box.TextChanged -= OnTextChanged;
        if (args.GetNewValue<bool>())
            box.TextChanged += OnTextChanged;
    }

    private static void OnTextChanged(object? sender, TextChangedEventArgs args)
    {
        if (sender is not TextBox box)
            return;

        _ = WiggleAsync(ResolveTarget(box));
    }

    private static async Task WiggleAsync(Visual target)
    {
        if (!Animating.Add(target))
            return;

        try
        {
            await MotionPlayer.PlayAsync(target, Wiggle);
        }
        finally
        {
            Animating.Remove(target);
        }
    }
}
