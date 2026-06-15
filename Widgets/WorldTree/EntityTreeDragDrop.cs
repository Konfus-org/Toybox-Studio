using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Toybox.Studio.Widgets.Ecs;
using Toybox.Studio.Widgets.Utils;

namespace Toybox.Studio.Widgets.WorldTree;

/// <summary>
/// Makes the world tree behave like an editable list: drag an entity by its handle to reorder it among its
/// siblings (drop on the upper/lower edge of a row) or reparent it (drop onto the middle of a row, or onto
/// empty space to move it to the root). The computed parent + index is handed to
/// <see cref="WorldViewModel.MoveEntityAsync"/>, which pushes it to the engine and re-syncs.
///
/// Two attached properties keep the view declarative (no code-behind routing): <c>Enabled</c> on the
/// <see cref="TreeView"/> wires the drop target, and <c>Handle</c> on a per-row grip starts the drag.
/// </summary>
public static class EntityTreeDragDrop
{
    // In-process drag payload: the dragged entity view-model itself (never serialized to the OS clipboard).
    private static readonly DataFormat<EntityViewModel> EntityFormat =
        DataFormat.CreateInProcessFormat<EntityViewModel>("toybox.entity");

    private const double DragThreshold = 4.0;

    private static readonly ConditionalWeakTable<Control, DragState> States = new();

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TreeView, bool>("Enabled", typeof(EntityTreeDragDrop));

    public static readonly AttachedProperty<bool> HandleProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Handle", typeof(EntityTreeDragDrop));

    // Marks a control (the Globals section) as a drop target that promotes the dragged entity to global.
    public static readonly AttachedProperty<bool> GlobalsZoneProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("GlobalsZone", typeof(EntityTreeDragDrop));

    static EntityTreeDragDrop()
    {
        EnabledProperty.Changed.AddClassHandler<TreeView>(OnEnabledChanged);
        HandleProperty.Changed.AddClassHandler<Control>(OnHandleChanged);
        GlobalsZoneProperty.Changed.AddClassHandler<Control>(OnGlobalsZoneChanged);
    }

    public static void SetEnabled(TreeView element, bool value) => element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(TreeView element) => element.GetValue(EnabledProperty);

    public static void SetHandle(Control element, bool value) => element.SetValue(HandleProperty, value);

    public static bool GetHandle(Control element) => element.GetValue(HandleProperty);

    public static void SetGlobalsZone(Control element, bool value) =>
        element.SetValue(GlobalsZoneProperty, value);

    public static bool GetGlobalsZone(Control element) => element.GetValue(GlobalsZoneProperty);

    private static void OnEnabledChanged(TreeView tree, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
        {
            DragDrop.SetAllowDrop(tree, true);
            tree.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            tree.AddHandler(DragDrop.DropEvent, OnDrop);
            tree.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        }
        else
        {
            tree.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
            tree.RemoveHandler(DragDrop.DropEvent, OnDrop);
            tree.RemoveHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        }
    }

    private static void OnGlobalsZoneChanged(Control zone, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
        {
            DragDrop.SetAllowDrop(zone, true);
            zone.AddHandler(DragDrop.DragOverEvent, OnGlobalsDragOver);
            zone.AddHandler(DragDrop.DropEvent, OnGlobalsDrop);
            zone.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        }
        else
        {
            zone.RemoveHandler(DragDrop.DragOverEvent, OnGlobalsDragOver);
            zone.RemoveHandler(DragDrop.DropEvent, OnGlobalsDrop);
            zone.RemoveHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        }
    }

    private static void OnHandleChanged(Control handle, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
        {
            handle.AddHandler(InputElement.PointerPressedEvent, OnHandlePressed, RoutingStrategies.Tunnel);
            handle.AddHandler(InputElement.PointerMovedEvent, OnHandleMoved, RoutingStrategies.Tunnel);
        }
        else
        {
            handle.RemoveHandler(InputElement.PointerPressedEvent, OnHandlePressed);
            handle.RemoveHandler(InputElement.PointerMovedEvent, OnHandleMoved);
            States.Remove(handle);
        }
    }

    private static void OnHandlePressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not Control handle || handle.DataContext is not EntityViewModel)
            return;
        if (!args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            return;

        var state = States.GetValue(handle, _ => new DragState());
        state.Armed = true;
        state.Start = args.GetPosition(handle);
        // The drag must be started from the press event (DoDragDropAsync takes it), so keep it until the
        // pointer moves past the threshold.
        state.Press = args;
        // Swallow the press so the grip doesn't also toggle expand/select while starting a drag.
        args.Handled = true;
    }

    private static async void OnHandleMoved(object? sender, PointerEventArgs args)
    {
        if (sender is not Control handle || !States.TryGetValue(handle, out var state) || !state.Armed)
            return;
        if (!args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            state.Armed = false;
            return;
        }

        var position = args.GetPosition(handle);
        if (Math.Abs(position.Y - state.Start.Y) < DragThreshold
            && Math.Abs(position.X - state.Start.X) < DragThreshold)
            return;
        if (handle.DataContext is not EntityViewModel item || state.Press is null)
        {
            state.Armed = false;
            return;
        }

        var press = state.Press;
        state.Armed = false;
        state.Press = null;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(EntityFormat, item));
        await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Move);
    }

    private static void OnDragOver(object? sender, DragEventArgs args)
    {
        var dragging = args.DataTransfer?.Contains(EntityFormat) == true;
        args.DragEffects = dragging ? DragDropEffects.Move : DragDropEffects.None;
        args.Handled = true;
        if (!dragging)
            return;

        // Mirror the drop logic's target so the indicator shows exactly where it will land: a highlight on
        // the row being reparented onto, or an insertion line at the row edge being reordered to.
        var row = (args.Source as Visual)?.FindAncestorOfType<TreeViewItem>();
        if (row is null)
        {
            DropIndicator.Clear();
            return;
        }

        DropIndicator.Show(row, Classify(row, args) switch
        {
            DropPosition.Before => DropMarker.Before,
            DropPosition.After => DropMarker.After,
            _ => DropMarker.Onto,
        });
    }

    private static async void OnDrop(object? sender, DragEventArgs args)
    {
        DropIndicator.Clear();
        if (sender is not TreeView { DataContext: WorldViewModel world })
            return;
        if (args.DataTransfer?.TryGetValue(EntityFormat) is not { } dragged)
            return;

        args.Handled = true;

        // A global dragged back into the scene tree is demoted to an ordinary entity; it keeps its current
        // parent/order, so it simply reappears in the tree.
        if (dragged.IsGlobal)
        {
            await world.SetEntityGlobalAsync(dragged.Id, false);
            return;
        }

        var row = (args.Source as Visual)?.FindAncestorOfType<TreeViewItem>();
        var target = row?.DataContext as EntityViewModel;

        ulong parentId;
        int index;
        if (target is null)
        {
            // Dropped on empty space → move to the root and append.
            parentId = 0UL;
            index = int.MaxValue;
        }
        else if (ReferenceEquals(target, dragged))
        {
            return;
        }
        else if (Classify(row!, args) == DropPosition.Onto)
        {
            // Dropped onto a row → become its (last) child.
            parentId = target.Id;
            index = int.MaxValue;
        }
        else
        {
            // Dropped on a row edge → become a sibling, before or after the target. Index is computed in
            // the sibling list with the dragged entity removed, matching the engine's renumber.
            var siblings = (target.Parent?.Children ?? world.RootEntities)
                .Where(sibling => !ReferenceEquals(sibling, dragged))
                .ToList();
            var slot = siblings.IndexOf(target);
            if (slot < 0)
                slot = siblings.Count;
            index = Classify(row!, args) == DropPosition.After ? slot + 1 : slot;
            parentId = target.Parent?.Id ?? 0UL;
        }

        await world.MoveEntityAsync(dragged.Id, parentId, index);
    }

    // The Globals section is a single drop target: dropping any entity onto it promotes that entity to
    // global. The whole zone highlights while a drag hovers it.
    private static void OnGlobalsDragOver(object? sender, DragEventArgs args)
    {
        var dragging = args.DataTransfer?.Contains(EntityFormat) == true;
        args.DragEffects = dragging ? DragDropEffects.Move : DragDropEffects.None;
        args.Handled = true;
        if (dragging && sender is Control zone)
            DropIndicator.Show(zone, DropMarker.Onto);
    }

    private static async void OnGlobalsDrop(object? sender, DragEventArgs args)
    {
        DropIndicator.Clear();
        if (sender is not Control { DataContext: WorldViewModel world })
            return;
        if (args.DataTransfer?.TryGetValue(EntityFormat) is not { } dragged)
            return;

        args.Handled = true;
        if (!dragged.IsGlobal)
            await world.SetEntityGlobalAsync(dragged.Id, true);
    }

    private static void OnDragLeave(object? sender, DragEventArgs args) => DropIndicator.Clear();

    // The drop position within a row: its top third reorders before, its bottom third after, and the
    // middle reparents the dragged entity under this one.
    private static DropPosition Classify(Visual row, DragEventArgs args)
    {
        var height = row.Bounds.Height;
        if (height <= 0)
            return DropPosition.Onto;

        var y = args.GetPosition(row).Y;
        if (y < height * 0.3)
            return DropPosition.Before;
        if (y > height * 0.7)
            return DropPosition.After;
        return DropPosition.Onto;
    }

    private enum DropPosition
    {
        Before,
        Onto,
        After,
    }

    private sealed class DragState
    {
        public bool Armed;
        public Point Start;
        public PointerPressedEventArgs? Press;
    }
}
