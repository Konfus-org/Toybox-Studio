using Avalonia.Controls;
using Avalonia;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// Makes a collapsible item nod in the direction it just moved (a <see cref="NodClip"/>): a small downward
/// dip when it EXPANDS and an upward bob when it COLLAPSES, then settle. Bind <see cref="StateProperty"/>
/// to the item's expanded flag (a section header's <c>IsChecked</c>, a tree node's <c>IsExpanded</c>) and
/// every section / category / row animates identically. The first value (initial binding, before the
/// control is loaded) is ignored so a default-expanded item doesn't nod on load — only real toggles animate.
/// </summary>
public static class ExpandNodBehavior
{
    public static readonly AttachedProperty<bool> StateProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("State", typeof(ExpandNodBehavior));

    // Down (positive) when opening, up (negative) when closing.
    private static readonly NodClip ExpandNod = new() { Depth = 4 };
    private static readonly NodClip CollapseNod = new() { Depth = -4 };

    static ExpandNodBehavior()
    {
        StateProperty.Changed.AddClassHandler<Control>(OnStateChanged);
    }

    public static void SetState(Control control, bool value) => control.SetValue(StateProperty, value);
    public static bool GetState(Control control) => control.GetValue(StateProperty);

    private static void OnStateChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        // The initial binding lands before the row is loaded; only animate real, post-load toggles so a
        // default-expanded section doesn't nod the moment the grid is built.
        if (!control.IsLoaded)
            return;

        _ = MotionPlayer.PlayAsync(control, args.GetNewValue<bool>() ? ExpandNod : CollapseNod);
    }
}
