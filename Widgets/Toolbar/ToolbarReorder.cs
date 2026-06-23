using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Toybox.Studio.Widgets.Behaviors;

namespace Toybox.Studio.Widgets.Toolbar;

/// <summary>
/// Turns a tool button into a drag-to-reorder gesture within its <see cref="ToolbarViewModel"/>.
/// Attach <c>local:ToolbarReorder.Enabled="True"</c> on the tool control (its DataContext is the
/// <see cref="ToolbarItemViewModel"/>); an ancestor <see cref="ItemsControl"/> must be bound to
/// <see cref="ToolbarViewModel.Tools"/>. Orientation-aware: it reorders along X when the toolbar is
/// horizontal and along Y when vertical. A press without a drag falls through to the button's click (running
/// the tool); a drag past the threshold captures, shows the insertion line, and commits a single
/// <see cref="ToolbarViewModel.MoveTool"/> on release (suppressing the click).
/// </summary>
public static class ToolbarReorder
{
    private const double DragThreshold = 4.0;

    private static readonly ConditionalWeakTable<Control, DragState> States = new();

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(ToolbarReorder));

    static ToolbarReorder()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);
    }

    public static void SetEnabled(Control element, bool value) => element.SetValue(EnabledProperty, value);

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
        if (items?.DataContext is not ToolbarViewModel list
            || handle.DataContext is not ToolbarItemViewModel element)
            return;

        var from = -1;
        for (var index = 0; index < list.Tools.Count; index++)
        {
            if (ReferenceEquals(list.Tools[index], element))
            {
                from = index;
                break;
            }
        }

        if (from < 0)
            return;

        var state = States.GetValue(handle, _ => new DragState());
        state.Armed = true;
        state.Dragging = false;
        state.Items = items;
        state.List = list;
        state.From = from;
        state.Start = args.GetPosition(items);
    }

    private static void OnMoved(object? sender, PointerEventArgs args)
    {
        if (sender is not Control handle || !States.TryGetValue(handle, out var state) || !state.Armed)
            return;
        if (state.Items is null || state.List is null)
            return;

        var horizontal = state.List.Orientation == Orientation.Horizontal;
        var position = args.GetPosition(state.Items);
        if (!state.Dragging)
        {
            var moved = horizontal ? Math.Abs(position.X - state.Start.X) : Math.Abs(position.Y - state.Start.Y);
            if (moved < DragThreshold)
                return;

            state.Dragging = true;
            args.Pointer.Capture(handle);
            handle.Cursor = new Cursor(horizontal ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth);
        }

        ShowIndicator(state.Items, ComputeTargetIndex(state.Items, position, horizontal), horizontal);
        args.Handled = true;
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is not Control handle || !States.TryGetValue(handle, out var state))
            return;

        if (state.Dragging && state.Items is not null && state.List is not null)
        {
            var horizontal = state.List.Orientation == Orientation.Horizontal;
            var to = ComputeTargetIndex(state.Items, args.GetPosition(state.Items), horizontal);
            if (to >= 0)
                state.List.MoveTool(state.From, to);

            args.Pointer.Capture(null);
            handle.Cursor = Cursor.Default;
            args.Handled = true; // Suppress the button's click — this gesture was a reorder, not a tap.
        }

        DropIndicator.Clear();
        state.Armed = false;
        state.Dragging = false;
        state.Items = null;
        state.List = null;
    }

    private static void ShowIndicator(ItemsControl items, int index, bool horizontal)
    {
        var count = items.ItemCount;
        if (count == 0 || index < 0)
        {
            DropIndicator.Clear();
            return;
        }

        if (index >= count && items.ContainerFromIndex(count - 1) is { } last)
            DropIndicator.Show(last, horizontal ? DropMarker.Right : DropMarker.After);
        else if (items.ContainerFromIndex(index) is { } container)
            DropIndicator.Show(container, horizontal ? DropMarker.Left : DropMarker.Before);
    }

    // The drop index is the number of items whose midpoint (along the layout axis) sits before the pointer,
    // clamped to the list. Uses realized containers (the toolbar is small and unvirtualized).
    private static int ComputeTargetIndex(ItemsControl items, Point pointer, bool horizontal)
    {
        var count = items.ItemCount;
        if (count == 0)
            return -1;

        var before = 0;
        foreach (var container in items.GetRealizedContainers())
        {
            var origin = container.TranslatePoint(default, items) ?? default;
            var mid = horizontal
                ? origin.X + container.Bounds.Width / 2
                : origin.Y + container.Bounds.Height / 2;
            var along = horizontal ? pointer.X : pointer.Y;
            if (along > mid)
                before++;
        }

        return Math.Clamp(before, 0, count - 1);
    }

    private sealed class DragState
    {
        public bool Armed;
        public bool Dragging;
        public ItemsControl? Items;
        public ToolbarViewModel? List;
        public int From;
        public Point Start;
    }
}
