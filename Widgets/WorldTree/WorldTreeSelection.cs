using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Toybox.Studio.Services.World;
using Toybox.Studio.Widgets.Ecs;

namespace Toybox.Studio.Widgets.WorldTree;

/// <summary>
/// Drives multi-selection across the world view's selectors (the Globals and Streamed trees and the flat
/// search list) from the single shared <see cref="WorldSelection"/>. Each control runs Avalonia's native
/// multi-select interaction (plain click replaces, Ctrl toggles, Shift range-selects); this behavior merges
/// every control's selection into the shared set and, on any external change (a viewport pick, a world
/// refresh), pushes the canonical set back into each control so all three selectors and the viewport agree.
///
/// Bind <c>wt:WorldTreeSelection.Selection="{Binding Selection}"</c> on every selector that shares the
/// selection. Like <see cref="EntityTreeDragDrop"/>, it is entity-aware so it can resolve ids to the row
/// view-models a tree highlights, keeping the view free of code-behind routing.
/// </summary>
public static class WorldTreeSelection
{
    public static readonly AttachedProperty<WorldSelection?> SelectionProperty =
        AvaloniaProperty.RegisterAttached<Control, WorldSelection?>("Selection", typeof(WorldTreeSelection));

    private static readonly ConditionalWeakTable<WorldSelection, Group> Groups = new();
    private static readonly ConditionalWeakTable<Control, Group> ControlGroups = new();

    static WorldTreeSelection() => SelectionProperty.Changed.AddClassHandler<Control>(OnSelectionChanged);

    public static void SetSelection(Control control, WorldSelection? value) =>
        control.SetValue(SelectionProperty, value);

    public static WorldSelection? GetSelection(Control control) => control.GetValue(SelectionProperty);

    private static void OnSelectionChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        // Bound once when the view materializes; ignore a redundant re-set of the same instance.
        if (ControlGroups.TryGetValue(control, out _))
            return;

        if (args.GetNewValue<WorldSelection?>() is not { } selection)
            return;

        var group = Groups.GetValue(selection, static s =>
        {
            var created = new Group(s);
            s.SelectionChanged += () => PushToControls(created);
            return created;
        });

        group.Controls.Add(control);
        ControlGroups.Add(control, group);

        SetSelectionMode(control, SelectionMode.Multiple);

        // Capture the gesture's modifiers before the control mutates its own selection.
        control.AddHandler(
            InputElement.PointerPressedEvent, (_, e) => group.Modifiers = e.KeyModifiers,
            RoutingStrategies.Tunnel);
        control.AddHandler(
            InputElement.KeyDownEvent, (_, e) => group.Modifiers = e.KeyModifiers,
            RoutingStrategies.Tunnel);

        WireSelectionChanged(control, group);

        // Reflect whatever is already selected (e.g. a viewport pick made before this tree loaded).
        PushToControl(control, group);
    }

    // A genuine user selection in one control: merge it with the others and publish the shared set. A plain
    // (unmodified) change replaces the whole selection — including clearing the other tree — while a Ctrl/Shift
    // change adds to it. The just-touched rows become primary (last), so the inspector follows them.
    private static void OnUserSelectionChanged(Control control, Group group, SelectionChangedEventArgs e)
    {
        // Ignore our own programmatic writes and the transient changes a world reconcile raises while it
        // rebuilds the rows — only a real user gesture should republish the shared selection.
        if (group.Suppress > 0 || group.Selection.IsBatching)
            return;

        var ids = new List<ulong>();
        if (group.Additive)
        {
            foreach (var member in group.Controls)
                foreach (var id in SelectedIdsOf(member))
                    if (!ids.Contains(id))
                        ids.Add(id);
        }
        else
        {
            ids.AddRange(SelectedIdsOf(control));
        }

        // Make the newly added rows primary (the active entity is the last id), so the inspector and the
        // local-space gizmo follow the row the user just clicked.
        foreach (var added in e.AddedItems.OfType<EntityViewModel>())
        {
            ids.Remove(added.Id);
            ids.Add(added.Id);
        }

        group.Selection.SetMany(ids);
    }

    // The shared selection changed (a user edit above, a viewport pick, or a world refresh): mirror it into
    // every control so all selectors highlight the same rows.
    private static void PushToControls(Group group)
    {
        foreach (var control in group.Controls)
            PushToControl(control, group);
    }

    private static void PushToControl(Control control, Group group)
    {
        var wanted = new HashSet<ulong>(group.Selection.SelectedIds);
        var items = ResolveItems(control, wanted);

        var selected = SelectedItemsOf(control);
        if (selected is null)
            return;

        group.Suppress++;
        try
        {
            selected.Clear();
            foreach (var item in items)
                selected.Add(item);
        }
        finally
        {
            group.Suppress--;
        }

        // A TreeView won't auto-scroll to a programmatic selection (e.g. an entity picked in the viewport or
        // freshly added at the end); bring the active row into view once its container is realized.
        if (control is TreeView tree && group.Selection.PrimaryId is { } primary)
            Dispatcher.UIThread.Post(() => BringIntoView(tree, primary, items), DispatcherPriority.Loaded);
    }

    private static void BringIntoView(TreeView tree, ulong primary, IReadOnlyList<EntityViewModel> items)
    {
        if (items.FirstOrDefault(item => item.Id == primary) is { } row
            && tree.ContainerFromItem(row) is { } container)
            container.BringIntoView();
    }

    // The entity view-models in this control whose id is currently selected. A tree is hierarchical, so it is
    // walked depth-first; a flat list (the search results) only offers its top-level rows.
    private static List<EntityViewModel> ResolveItems(Control control, HashSet<ulong> wanted)
    {
        var matches = new List<EntityViewModel>();
        if (wanted.Count == 0 || ItemsOf(control) is not { } roots)
            return matches;

        if (control is TreeView)
            CollectTree(roots, wanted, matches);
        else
            foreach (var item in roots.OfType<EntityViewModel>())
                if (wanted.Contains(item.Id))
                    matches.Add(item);

        return matches;
    }

    private static void CollectTree(
        IEnumerable roots, HashSet<ulong> wanted, List<EntityViewModel> matches)
    {
        foreach (var item in roots.OfType<EntityViewModel>())
        {
            if (wanted.Contains(item.Id))
                matches.Add(item);
            CollectTree(item.Children, wanted, matches);
        }
    }

    private static IEnumerable<ulong> SelectedIdsOf(Control control) =>
        (SelectedItemsOf(control)?.OfType<EntityViewModel>() ?? [])
        .Select(item => item.Id)
        .ToList();

    private static IList? SelectedItemsOf(Control control) => control switch
    {
        TreeView tree => tree.SelectedItems,
        ListBox list => list.SelectedItems,
        _ => null,
    };

    private static IEnumerable? ItemsOf(Control control) => control switch
    {
        ItemsControl items => items.ItemsSource ?? items.Items,
        _ => null,
    };

    private static void SetSelectionMode(Control control, SelectionMode mode)
    {
        switch (control)
        {
            case TreeView tree:
                tree.SelectionMode = mode;
                break;
            case ListBox list:
                list.SelectionMode = mode;
                break;
        }
    }

    private static void WireSelectionChanged(Control control, Group group)
    {
        switch (control)
        {
            case TreeView tree:
                tree.SelectionChanged += (_, e) => OnUserSelectionChanged(control, group, e);
                break;
            case ListBox list:
                list.SelectionChanged += (_, e) => OnUserSelectionChanged(control, group, e);
                break;
        }
    }

    // One coordinator per shared WorldSelection: it owns the participating controls and the suppression flag
    // that keeps a programmatic push (selection -> controls) from looping back as a user edit.
    private sealed class Group
    {
        public readonly WorldSelection Selection;
        public readonly List<Control> Controls = [];

        // Re-entrancy guard: raised while we write a control's native selection, so the SelectionChanged that
        // write raises is recognised as our own and not re-published as a user selection.
        public int Suppress;

        // The keyboard modifiers at the moment of the gesture that is about to change a control's selection,
        // captured on the tunnelling press/key so the SelectionChanged handler can tell a replace (plain) from
        // an additive (Ctrl/Shift) edit — including across two different trees.
        public KeyModifiers Modifiers;

        public Group(WorldSelection selection) => Selection = selection;

        public bool Additive => (Modifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0;
    }
}
