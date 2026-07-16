using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia;
using Toybox.Studio.PropertyGrid.Slots;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The drag-to-reorder input glue for a list element's <see cref="ReorderHandleViewModel"/> view: press
/// the handle, drag past a small threshold to arm (so a stray click never reorders), and the element row
/// follows the pointer — each time the pointer crosses into another row's slot the handle commits a
/// <c>Move</c>, so the list and its rows reorder live under the drag. Attach with
/// <c>ListReorder.Enabled="True"</c> on the handle view's root, whose DataContext is the handle.
/// </summary>
public static class ListReorder
{
    private const double ArmThreshold = 4;

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(ListReorder));

    // One drag runs at a time (the handle captures the pointer), so the state is a single slot.
    private static DragState? _drag;

    static ListReorder()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);
    }

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool>())
        {
            control.PointerPressed += OnPointerPressed;
            control.PointerMoved += OnPointerMoved;
            control.PointerReleased += OnPointerReleased;
        }
        else
        {
            control.PointerPressed -= OnPointerPressed;
            control.PointerMoved -= OnPointerMoved;
            control.PointerReleased -= OnPointerReleased;
        }
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control handle
            || handle.DataContext is not ReorderHandleViewModel reorder
            || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed
            || FindElementList(handle) is not { } list)
        {
            return;
        }

        _drag = new DragState(handle, reorder, list, e.GetPosition(list));
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_drag is not { } drag || !ReferenceEquals(sender, drag.Handle))
            return;

        var position = e.GetPosition(drag.List);
        if (!drag.Armed)
        {
            if (Math.Abs(position.Y - drag.Start.Y) < ArmThreshold)
                return;

            drag.Armed = true;
        }

        var target = IndexAt(drag.List, position.Y);
        if (target >= 0 && target != drag.Reorder.Index)
            drag.Reorder.Move(drag.Reorder.Index, target);
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag is null || !ReferenceEquals(sender, _drag.Handle))
            return;

        e.Pointer.Capture(null);
        _drag = null;
    }

    /// <summary>
    /// The ItemsControl presenting the element rows the dragged handle belongs to: the nearest ancestor
    /// showing property NODES — matching on the items' type keeps a future ItemsControl inside a slot
    /// view from being mistaken for it.
    /// </summary>
    private static ItemsControl? FindElementList(Control handle) =>
        handle.GetVisualAncestors()
            .OfType<ItemsControl>()
            .FirstOrDefault(items => items.ItemsSource is IEnumerable<PropertyNode>);

    /// <summary>The index of the element row under the given y (relative to the list), or -1.</summary>
    private static int IndexAt(ItemsControl list, double y)
    {
        for (var i = 0; i < list.ItemCount; i++)
        {
            if (list.ContainerFromIndex(i) is not { } container
                || container.TranslatePoint(new Point(0, 0), list) is not { } top)
            {
                continue;
            }

            if (y >= top.Y && y < top.Y + container.Bounds.Height)
                return i;
        }

        return -1;
    }

    private sealed class DragState(Control handle, ReorderHandleViewModel reorder, ItemsControl list, Point start)
    {
        public Control Handle { get; } = handle;

        public ReorderHandleViewModel Reorder { get; } = reorder;

        public ItemsControl List { get; } = list;

        public Point Start { get; } = start;

        public bool Armed { get; set; }
    }
}
