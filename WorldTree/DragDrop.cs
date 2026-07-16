using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Runtime.CompilerServices;
using Toybox.Studio.Behaviors;

namespace Toybox.Studio.WorldTree;

/// <summary>
/// Makes the world tree behave like an editable list: drag an entity by its handle to reorder it among its
/// siblings (drop on the upper/lower edge of a row) or reparent it (drop onto the middle of a row, or onto
/// empty space to move it to the root), or drop it on the Globals section to promote it to global. The
/// computed parent + index is handed to <see cref="WorldTreeViewModel.MoveEntity"/>, which reparents/reorders
/// it engine-side and re-syncs.
///
/// The gesture is a manual pointer-capture drag — the same pattern the property-grid list reorder uses —
/// rather than the OS drag-drop loop: arm on press, capture once a small movement threshold is crossed,
/// hit-test the row under the pointer each move to drive the drop indicator, and commit one move on release.
/// (Avalonia 12's <c>DoDragDropAsync</c> did not deliver the drop here, leaving the row to snap back.)
///
/// Two attached properties keep the view declarative (no code-behind routing): <c>Handle</c> on a per-row
/// grip starts the drag, and <c>GlobalsZone</c> marks the Globals section so a drop there promotes to global.
/// </summary>
public static class DragDrop
{
    private const double DragThreshold = 4.0;

    private static readonly ConditionalWeakTable<Control, DragState> States = new();

    public static readonly AttachedProperty<bool> HandleProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Handle", typeof(DragDrop));

    // Marks the Globals section so the drag can recognise a drop there (promote the dragged entity to global)
    // and light the whole zone while a drag hovers it.
    public static readonly AttachedProperty<bool> GlobalsZoneProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("GlobalsZone", typeof(DragDrop));

    static DragDrop()
    {
        HandleProperty.Changed.AddClassHandler<Control>(OnHandleChanged);
    }

    public static void SetHandle(Control element, bool value) => element.SetValue(HandleProperty, value);

    public static bool GetHandle(Control element) => element.GetValue(HandleProperty);

    public static void SetGlobalsZone(Control element, bool value) =>
        element.SetValue(GlobalsZoneProperty, value);

    public static bool GetGlobalsZone(Control element) => element.GetValue(GlobalsZoneProperty);

    private static void OnHandleChanged(Control handle, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
        {
            handle.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
            handle.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
            handle.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
        }
        else
        {
            handle.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
            handle.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
            handle.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
            States.Remove(handle);
        }
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not Control handle || handle.DataContext is not WorldEntityViewModel dragged)
            return;
        if (!args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            return;

        // The whole world panel is the hit-test surface (it contains both trees and the Globals zone); its
        // DataContext is the shared WorldTreeViewModel the move commits against.
        if (handle.FindAncestorOfType<WorldTreeView>() is not { DataContext: WorldTreeViewModel world } root)
            return;

        var state = States.GetValue(handle, _ => new DragState());
        state.Armed = true;
        state.Dragging = false;
        state.Dragged = dragged;
        state.World = world;
        state.Root = root;
        state.Start = args.GetPosition(root);

        // Swallow the press so the grip doesn't also select the row or toggle expand while starting a drag.
        args.Handled = true;
    }

    private static void OnMoved(object? sender, PointerEventArgs args)
    {
        if (sender is not Control handle || !States.TryGetValue(handle, out var state) || !state.Armed)
            return;
        if (state.Root is null)
            return;
        if (!args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            state.Armed = false;
            return;
        }

        if (!state.Dragging)
        {
            var position = args.GetPosition(state.Root);
            if (Math.Abs(position.Y - state.Start.Y) < DragThreshold
                && Math.Abs(position.X - state.Start.X) < DragThreshold)
                return;

            state.Dragging = true;
            args.Pointer.Capture(handle);
            handle.Cursor = new Cursor(StandardCursorType.SizeAll);
        }

        // Mirror the drop logic's target so the indicator shows exactly where it will land.
        var hit = Resolve(state, args);
        if (hit.Tree is null && hit.GlobalsZone is null)
            DropIndicator.Clear();
        else if (hit.Tree is null)
            DropIndicator.Show(hit.GlobalsZone!, DropMarker.Onto); // over the Globals zone → promote
        else if (hit.Row is null)
            DropIndicator.Clear(); // over a tree but no row (empty area) → append, no insertion line
        else
            DropIndicator.Show(hit.Row, hit.Position switch
            {
                DropPosition.Before => DropMarker.Before,
                DropPosition.After => DropMarker.After,
                _ => DropMarker.Onto,
            });

        args.Handled = true;
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is not Control handle || !States.TryGetValue(handle, out var state))
            return;

        var commit = state.Dragging && state.World is { } world && state.Dragged is { } dragged
            ? (World: world, Dragged: dragged, Target: Resolve(state, args))
            : default;

        if (state.Dragging)
        {
            args.Pointer.Capture(null);
            handle.Cursor = Cursor.Default;
            args.Handled = true;
        }

        DropIndicator.Clear();
        state.Armed = false;
        state.Dragging = false;
        state.Dragged = null;
        state.World = null;
        state.Root = null;

        if (commit.World is not null)
            Commit(commit.World, commit.Dragged, commit.Target);
    }

    // Applies the drop: reorder/reparent within a bucket, append to a bucket root (empty area), or promote to
    // global (Globals zone). The engine move + refresh reconcile the tree to the new arrangement.
    private static void Commit(WorldTreeViewModel world, WorldEntityViewModel dragged, DropTarget hit)
    {
        // Dropped outside every tree and the Globals zone → nothing to do.
        if (hit.Tree is null && hit.GlobalsZone is null)
            return;

        // Over the Globals zone but not over a tree row → promote to global root (a no-op if already global).
        if (hit.Tree is null)
        {
            if (!dragged.IsGlobal)
                world.MoveEntity(dragged.Id, parentId: 0UL, index: int.MaxValue, global: true);
            return;
        }

        // Which forest the entity is landing in. Dropping into the other bucket also flips the entity's global
        // flag so it lands where it was dropped.
        var targetIsGlobal = ReferenceEquals(hit.Tree.ItemsSource, world.Globals);
        var bucketRoots = targetIsGlobal ? world.Globals : world.Streamed;
        var target = hit.Entity;

        ulong parentId;
        int index;
        if (target is null)
        {
            // Dropped on a tree's empty space → move to the root of this bucket and append.
            parentId = 0UL;
            index = int.MaxValue;
        }
        else if (ReferenceEquals(target, dragged))
        {
            // No move needed, but still honour a bucket change (dropped onto itself in the other list).
            if (dragged.IsGlobal != targetIsGlobal)
                world.MoveEntity(dragged.Id, parentId: 0UL, index: int.MaxValue, global: targetIsGlobal);
            return;
        }
        else if (hit.Position == DropPosition.Onto)
        {
            // Dropped onto a row → become its (last) child.
            parentId = target.Id;
            index = int.MaxValue;
        }
        else
        {
            // Dropped on a row edge → become a sibling, before or after the target. Index is computed in the
            // sibling list with the dragged entity removed, matching the engine's renumber.
            var siblings = (target.Parent?.Children ?? bucketRoots)
                .Where(sibling => !ReferenceEquals(sibling, dragged))
                .ToList();
            var slot = siblings.IndexOf(target);
            if (slot < 0)
                slot = siblings.Count;
            index = hit.Position == DropPosition.After ? slot + 1 : slot;
            parentId = target.Parent?.Id ?? 0UL;
        }

        var changeBucket = dragged.IsGlobal != targetIsGlobal;
        world.MoveEntity(dragged.Id, parentId, index, changeBucket ? targetIsGlobal : null);
    }

    // Hit-tests the current pointer position against the world panel to find the row / tree / Globals zone it
    // is over, plus where within a row it would drop. The pointer is captured by the grip during a drag, so
    // we hit-test the view ourselves rather than relying on the event's source.
    private static DropTarget Resolve(DragState state, PointerEventArgs args)
    {
        var root = state.Root!;
        var hit = root.InputHitTest(args.GetPosition(root)) as Visual;
        if (hit is null)
            return default;

        var ancestors = hit.GetSelfAndVisualAncestors().OfType<Control>().ToList();
        var row = ancestors.OfType<Border>().FirstOrDefault(border => border.Classes.Contains("entityRow"));
        var tree = ancestors.OfType<TreeView>().FirstOrDefault();
        var zone = ancestors.FirstOrDefault(GetGlobalsZone);
        var position = row is not null ? Classify(row, args) : DropPosition.Onto;
        return new DropTarget(row, row?.DataContext as WorldEntityViewModel, tree, zone, position);
    }

    // The drop position within a row: its top third reorders before, its bottom third after, and the
    // middle reparents the dragged entity under this one.
    private static DropPosition Classify(Visual row, PointerEventArgs args)
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

    // The resolved drop context under the pointer: the row Border and its entity (null off any row), the tree
    // the pointer is over (null outside both forests), the Globals zone (null outside it), and where in the
    // row it would land.
    private readonly record struct DropTarget(
        Border? Row,
        WorldEntityViewModel? Entity,
        TreeView? Tree,
        Control? GlobalsZone,
        DropPosition Position);

    private sealed class DragState
    {
        public bool Armed;
        public bool Dragging;
        public WorldEntityViewModel? Dragged;
        public WorldTreeViewModel? World;
        public Control? Root;
        public Point Start;
    }
}
