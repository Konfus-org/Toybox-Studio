using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Toybox.Studio.Widgets.Utils;

/// <summary>
/// Two-way binds a list/tree's selected item to a shared value without the usual clobber when several
/// selectors share one selection: when the shared value is pushed into a control that does not contain it
/// (e.g. selecting a tree node clears the Globals list), the control's internal reset-to-null is suppressed
/// instead of being written back over the real selection.
///
/// Bind with <c>util:SharedSelection.Selected="{Binding SelectedEntity, Mode=TwoWay}"</c> on every control
/// that shares the selection.
/// </summary>
public static class SharedSelection
{
    private sealed class State
    {
        public bool Wired;
        public bool Suppress;
    }

    private static readonly ConditionalWeakTable<SelectingItemsControl, State> States = new();

    public static readonly AttachedProperty<object?> SelectedProperty =
        AvaloniaProperty.RegisterAttached<Control, object?>("Selected", typeof(SharedSelection));

    static SharedSelection() =>
        SelectedProperty.Changed.AddClassHandler<Control>(OnSharedChanged);

    public static void SetSelected(Control control, object? value) =>
        control.SetValue(SelectedProperty, value);

    public static object? GetSelected(Control control) => control.GetValue(SelectedProperty);

    private static void OnSharedChanged(Control element, AvaloniaPropertyChangedEventArgs args)
    {
        if (element is not SelectingItemsControl control)
            return;

        var state = States.GetValue(control, _ => new State());
        if (!state.Wired)
        {
            state.Wired = true;
            control.SelectionChanged += (_, _) =>
            {
                // A genuine user selection in this control flows back to the shared value; a programmatic
                // reset (from the push below) is suppressed so it can't clear the real selection.
                if (!state.Suppress)
                    SetSelected(control, control.SelectedItem);
            };
        }

        // Push the shared value into the control. If it isn't one of this control's items, the control
        // resets to null internally — which the suppression flag keeps from writing back.
        state.Suppress = true;
        try
        {
            control.SelectedItem = args.GetNewValue<object?>();
        }
        finally
        {
            state.Suppress = false;
        }
    }
}
