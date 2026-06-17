using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace Toybox.Studio.Widgets.Behaviors;

/// <summary>
/// Two-way binds a list/tree's selected item to a shared value without the usual clobber when several
/// selectors share one selection: when the shared value is pushed into a control that does not contain it
/// (e.g. selecting a tree node clears the Globals list), the control's internal reset-to-null is suppressed
/// instead of being written back over the real selection.
///
/// Works for both <see cref="TreeView"/> (which is NOT a <see cref="SelectingItemsControl"/> — it has its own
/// selection model) and ordinary <see cref="SelectingItemsControl"/>s like <see cref="ListBox"/>; both expose
/// a <c>SelectedItem</c> and a <c>SelectionChanged</c> event, so the behavior drives whichever it is given.
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

    // The property's default value. A plain null would equal the binding's initial push when nothing is
    // selected, so the property system would treat it as "no change" and never raise — leaving the control's
    // SelectionChanged handler unwired and tree clicks silently dropped (the inspector never updated). A
    // non-null sentinel guarantees the first binding push (even to null) is a real change that wires us up.
    private static readonly object Unset = new();

    private static readonly ConditionalWeakTable<Control, State> States = new();

    public static readonly AttachedProperty<object?> SelectedProperty =
        AvaloniaProperty.RegisterAttached<Control, object?>("Selected", typeof(SharedSelection), Unset);

    static SharedSelection() =>
        SelectedProperty.Changed.AddClassHandler<Control>(OnSharedChanged);

    public static void SetSelected(Control control, object? value) =>
        control.SetValue(SelectedProperty, value);

    public static object? GetSelected(Control control) => control.GetValue(SelectedProperty);

    private static void OnSharedChanged(Control element, AvaloniaPropertyChangedEventArgs args)
    {
        // Only the two selecting-control shapes we support; anything else carries no selection to share.
        if (element is not (TreeView or SelectingItemsControl))
            return;

        var state = States.GetValue(element, _ => new State());
        if (!state.Wired)
        {
            state.Wired = true;

            // A genuine user selection in this control flows back to the shared value; a programmatic reset
            // (from the push below) is suppressed so it can't clear the real selection.
            void OnSelectionChanged(object? _, SelectionChangedEventArgs __)
            {
                if (!state.Suppress)
                    SetSelected(element, SelectedItemOf(element));
            }

            if (element is TreeView tree)
                tree.SelectionChanged += OnSelectionChanged;
            else if (element is SelectingItemsControl selecting)
                selecting.SelectionChanged += OnSelectionChanged;
        }

        // Push the shared value into the control. If it isn't one of this control's items, the control
        // resets to null internally — which the suppression flag keeps from writing back.
        var value = args.GetNewValue<object?>();
        state.Suppress = true;
        try
        {
            SetSelectedItemOf(element, value);
        }
        finally
        {
            state.Suppress = false;
        }

        // A TreeView is not a SelectingItemsControl and so won't auto-scroll to a programmatic selection (e.g.
        // a freshly added entity appended at the end). Bring it into view once the container is realized;
        // BringIntoView is a no-op when the row is already visible, so this never disturbs a visible selection.
        if (element is TreeView && value is not null)
            Dispatcher.UIThread.Post(() => BringIntoView(element, value), DispatcherPriority.Loaded);
    }

    private static void BringIntoView(Control control, object item)
    {
        if (control is TreeView tree && tree.ContainerFromItem(item) is { } container)
            container.BringIntoView();
    }

    private static object? SelectedItemOf(Control control) => control switch
    {
        TreeView tree => tree.SelectedItem,
        SelectingItemsControl selecting => selecting.SelectedItem,
        _ => null,
    };

    private static void SetSelectedItemOf(Control control, object? value)
    {
        switch (control)
        {
            case TreeView tree:
                tree.SelectedItem = value;
                break;
            case SelectingItemsControl selecting:
                selecting.SelectedItem = value;
                break;
        }
    }
}
