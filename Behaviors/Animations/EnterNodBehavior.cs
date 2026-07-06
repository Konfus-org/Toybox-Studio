using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// On Enter, a single-line field gives a small vertical nod (a <see cref="NodClip"/> dip-and-back) to
/// confirm the input was accepted, then drops focus so the edit commits and the field is no longer active.
/// Multi-line fields (<c>AcceptsReturn</c>) are left alone — there Enter inserts a newline. The nod lands on
/// the same visible field the wiggle uses (<see cref="TextWiggleBehavior.ResolveTarget"/>, so the SearchBox
/// pill / NumericUpDown well nods, not the inner text); the focus drop happens regardless of animation
/// intensity (it's functional, not decorative). Enabled app-wide via a Style setter on TextBox in InputStyle.
/// </summary>
public static class EnterNodBehavior
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("Enabled", typeof(EnterNodBehavior));

    // A tighter dip than the house default: fields are small and the confirmation should read as a tap.
    private static readonly NodClip Nod = new() { Depth = 2.5, Duration = TimeSpan.FromMilliseconds(160) };

    static EnterNodBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<TextBox>(OnEnabledChanged);
    }

    public static void SetEnabled(TextBox box, bool value) => box.SetValue(EnabledProperty, value);
    public static bool GetEnabled(TextBox box) => box.GetValue(EnabledProperty);

    private static void OnEnabledChanged(TextBox box, AvaloniaPropertyChangedEventArgs args)
    {
        // Detach first so the handler is wired exactly once regardless of how the flag toggles.
        box.KeyDown -= OnKeyDown;
        if (args.GetNewValue<bool>())
            box.KeyDown += OnKeyDown;
    }

    private static void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (sender is not TextBox box || args.Key != Key.Enter || box.AcceptsReturn)
            return;

        _ = MotionPlayer.PlayAsync(TextWiggleBehavior.ResolveTarget(box), Nod);

        // Commit + deactivate: move focus off the field to the window root, which pushes any LostFocus-bound
        // text and leaves the field inactive. (This Avalonia build's IFocusManager has no ClearFocus, so we
        // redirect focus to the top level — made focusable — instead.)
        if (TopLevel.GetTopLevel(box) is { } top)
        {
            top.Focusable = true;
            top.Focus();
        }
    }
}
