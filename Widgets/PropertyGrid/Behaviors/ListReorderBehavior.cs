using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Toybox.Studio.Widgets.Behaviors;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Turns a per-row drag handle into a list reorder gesture: press the handle and drag up/down to move that
/// entry within its <see cref="ArrayPropertyViewModel"/>. Attach with <c>local:ListReorder.Enabled="True"</c>
/// on the handle control; the handle's DataContext must be the element view-model and an ancestor
/// <see cref="ItemsControl"/> must be bound to the list's <see cref="ArrayPropertyViewModel.Items"/>.
///
/// Modeled on <see cref="NumericScrub"/>: arm on press, capture once a small movement threshold is crossed,
/// and commit a single <see cref="ArrayPropertyViewModel.Move"/> on release (so a drag re-sends the property
/// once, not on every pointer move).
/// </summary>
public static class ListReorder
{
    private const double DragThreshold = 4.0;

    private static readonly ConditionalWeakTable<Control, DragState> States = new();

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(ListReorder));

    static ListReorder()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);
    }

    public static void SetEnabled(Control element, bool value) =>
        element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(Control element) => element.GetValue(EnabledProperty);

    private static void OnEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
        {
            control.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
        }
        else
        {
            control.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
            control.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
            control.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
            States.Remove(control);
        }
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not Control handle || !handle.IsEnabled)
            return;
        if (!args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            return;

        var items = handle.FindAncestorOfType<ItemsControl>();
        if (items is null)
            return;

        // The grip lives in its own row slot (DataContext = HandlePart) carrying its list + element; the older
        // inline grip had the element view-model as its DataContext and read the list off the ItemsControl.
        var (list, element) = handle.DataContext switch
        {
            HandlePart part => (part.List, (PropertyViewModel?)part.Element),
            PropertyViewModel pvm => (items.DataContext as ArrayPropertyViewModel, pvm),
            _ => (null, null),
        };
        if (list is null || element is null)
            return;

        var from = list.Items.IndexOf(element);
        if (from < 0)
            return;

        var state = States.GetValue(handle, _ => new DragState());
        state.Armed = true;
        state.Dragging = false;
        state.Items = items;
        state.List = list;
        state.From = from;
        state.StartY = args.GetPosition(items).Y;
    }

    private static void OnMoved(object? sender, PointerEventArgs args)
    {
        if (sender is not Control handle || !States.TryGetValue(handle, out var state) || !state.Armed)
            return;
        if (state.Items is null)
            return;

        var y = args.GetPosition(state.Items).Y;
        if (!state.Dragging)
        {
            if (Math.Abs(y - state.StartY) < DragThreshold)
                return;

            state.Dragging = true;
            args.Pointer.Capture(handle);
            handle.Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
        }

        // Show an insertion line where the row would land if dropped now.
        ShowIndicator(state.Items, ComputeTargetIndex(state.Items, y));
        args.Handled = true;
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is not Control handle || !States.TryGetValue(handle, out var state))
            return;

        if (state.Dragging && state.Items is not null && state.List is not null)
        {
            var to = ComputeTargetIndex(state.Items, args.GetPosition(state.Items).Y);
            if (to >= 0)
                state.List.Move(state.From, to);

            args.Pointer.Capture(null);
            handle.Cursor = Cursor.Default;
            args.Handled = true;
        }

        DropIndicator.Clear();
        state.Armed = false;
        state.Dragging = false;
        state.Items = null;
        state.List = null;
    }

    // Draws the insertion line at the boundary the drop index points at: above the container at that index,
    // or below the last container when dropping at the end.
    private static void ShowIndicator(ItemsControl items, int index)
    {
        var count = items.ItemCount;
        if (count == 0 || index < 0)
        {
            DropIndicator.Clear();
            return;
        }

        if (index >= count && items.ContainerFromIndex(count - 1) is { } last)
            DropIndicator.Show(last, DropMarker.After);
        else if (items.ContainerFromIndex(index) is { } container)
            DropIndicator.Show(container, DropMarker.Before);
    }

    // The drop index is the number of rows whose vertical midpoint sits above the pointer, clamped to the
    // list — i.e. where the dragged row would land if released here. Uses realized containers (the list is
    // small and unvirtualized) translated into the ItemsControl's own coordinate space.
    private static int ComputeTargetIndex(ItemsControl items, double pointerY)
    {
        var count = items.ItemCount;
        if (count == 0)
            return -1;

        var above = 0;
        foreach (var container in items.GetRealizedContainers())
        {
            var top = container.TranslatePoint(default, items)?.Y ?? 0;
            if (pointerY > top + container.Bounds.Height / 2)
                above++;
        }

        return Math.Clamp(above, 0, count - 1);
    }

    private sealed class DragState
    {
        public bool Armed;
        public bool Dragging;
        public ItemsControl? Items;
        public ArrayPropertyViewModel? List;
        public int From;
        public double StartY;
    }
}
