using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.Workspaces;

/// <summary>
/// Everything about layouts as a whole — never individual windows (that's the <see cref="Workspace"/>).
/// Builds the default layout from the registered dockables' slot/proportion metadata, persists and
/// lists named layouts through the <see cref="LayoutStore"/> it owns, rehydrates a saved layout back to
/// a live one (<see cref="Restore"/>: re-binding every tool's content, healing the scars a save
/// carries, re-pinning the center dock), and re-establishes the keep-the-window-dockable invariant
/// after every structural change Dock raises.
/// </summary>
public sealed class LayoutManager
{
    private readonly Workspace _workspace;
    private readonly DockFactory _factory;
    private readonly LayoutStore _store;
    private readonly Logger _log;

    // Guards the structural-change re-pinning: ReinforceCenter may graft a missing center dock, which
    // raises DockableAdded and would otherwise recurse back in.
    private bool _reinforcing;

    internal LayoutManager(Workspace workspace, DockFactory factory, Logger log)
    {
        _workspace = workspace;
        _factory = factory;
        _log = log;
        _store = new LayoutStore(log);

        // Every structural change (dock/undock/float/close) can reparent the center dock or empty out
        // its spine, so the keep-the-window-dockable invariant is re-established after each one.
        _factory.DockableAdded += (_, _) => ReinforceLive();
        _factory.DockableRemoved += (_, _) => ReinforceLive();
        _factory.DockableDocked += (_, _) => ReinforceLive();
        _factory.DockableUndocked += (_, _) => ReinforceLive();
        _factory.WindowAdded += (_, _) => ReinforceLive();
        _factory.WindowRemoved += (_, _) => ReinforceLive();
    }

    /// <summary>
    /// Builds the default layout — Left | [Top / Bottom] | Right, each dock recursively split by the
    /// dockables parented into it — from the workspace's slot/parent/proportion metadata, initialized
    /// and ready to go live. <see cref="DockSlot.Float"/> dockables are intentionally absent; they open
    /// on demand as floating windows at their declared bounds.
    /// </summary>
    public IRootDock CreateDefault()
    {
        // A fresh default layout abandons any previously open panels, so their view-models are dropped
        // (spawned ones disposed) and instance-id minting restarts before re-seeding from the catalog.
        _workspace.ResetInstances();

        var children = new List<IDockable>();
        AddWithSplitters(children, BuildSideColumn(DockSlot.Left), BuildCenter(), BuildSideColumn(DockSlot.Right));

        var main = _factory.CreateProportionalDock();
        main.Id = "MainLayout";
        main.Orientation = Orientation.Horizontal;
        main.VisibleDockables = _factory.CreateList(children.ToArray());
        main.ActiveDockable = children.FirstOrDefault(child => child is IDock and not IProportionalDockSplitter);

        var root = _factory.CreateRootDock();
        root.Id = "Root";
        root.IsCollapsable = false;
        root.VisibleDockables = _factory.CreateList<IDockable>(main);
        root.DefaultDockable = main;
        root.ActiveDockable = main;

        _factory.InitLayout(root);
        Attach(root);
        return root;
    }

    /// <summary>Reads a saved layout from disk — pure structure, not yet live. Null when the name is
    /// unknown or the file unreadable (the store logs the why); pair with <see cref="Restore"/>.</summary>
    public Task<IRootDock?> LoadAsync(string name) => _store.LoadAsync(name);

    /// <summary>Persists a layout under the given name.</summary>
    public Task SaveAsync(string name, IRootDock layout) => _store.SaveAsync(name, layout);

    /// <summary>Names of every stored layout, the working layout included.</summary>
    public Task<IReadOnlyList<string>> ListAsync() => _store.ListAsync();

    /// <summary>
    /// Rehydrates a freshly loaded layout into a live one: drops the outgoing panels' view-models,
    /// initializes the tree, re-binds every tool to its deferred view template, heals the scars a save
    /// can carry, and re-pins the center dock. False (with a logged warning) when the saved tree can't
    /// be brought up — the caller falls back to <see cref="CreateDefault"/>.
    /// </summary>
    public bool Restore(IRootDock layout)
    {
        try
        {
            _workspace.ResetInstances();
            _factory.InitLayout(layout);
            Attach(layout);
            Heal(layout);
            ReinforceCenter(layout);
            return true;
        }
        catch (Exception exception)
        {
            // A saved layout that can't be rehydrated (schema drift, partial file) must not take the
            // editor down; the caller swaps in the default instead.
            _log.Warning($"Saved dock layout could not be restored: {exception.Message}");
            return false;
        }
    }

    // Walks a layout (floating windows and the root's pinned/hidden collections included) and re-binds
    // every known tool to its deferred view template via the window manager. Runs after both building
    // the default layout and deserializing a saved one; idempotent.
    private void Attach(IDockable? dockable)
    {
        if (dockable is null)
            return;

        if (dockable is Tool tool)
            _workspace.AttachTool(tool);

        if (dockable is IDock dock && dock.VisibleDockables is { } children)
        {
            foreach (var child in children)
                Attach(child);
        }

        if (dockable is IRootDock root)
        {
            if (root.Windows is { } windows)
            {
                foreach (var window in windows)
                    Attach(window.Layout);
            }

            // Pinned and hidden tools live in the root's own collections, NOT in any dock's
            // VisibleDockables, so the recursion above skips them. Without this their deferred view
            // template is never re-bound on restore and they reveal blank. Re-bind them too.
            foreach (var pinned in DockTree.PinnedAndHidden(root))
                Attach(pinned);
        }
    }

    // Heals the scars a saved layout can carry before it goes live. Dock's collapse bookkeeping saves
    // a collapsed dock with Proportion 0 — restored, it renders at zero size and silently swallows
    // every panel docked (or later opened) into it; those revert to auto so the panel redistributes.
    // The main root also drops a stale floating-window back-reference — it is hosted by the
    // DockControl, never by a float. (Collapse pins are handled by ReinforceCenter, which runs after.)
    private static void Heal(IRootDock root)
    {
        root.Window = null;
        HealProportions(root);

        if (root.Windows is { } windows)
        {
            foreach (var window in windows)
            {
                if (window.Layout is { } layout)
                    HealProportions(layout);
            }
        }
    }

    private static void HealProportions(IDock dock)
    {
        if (dock.VisibleDockables is not { } dockables)
            return;

        foreach (var child in dockables)
        {
            if (child is not IDock childDock || child is IProportionalDockSplitter)
                continue;

            if (childDock.Proportion == 0.0)
                childDock.Proportion = double.NaN;
            HealProportions(childDock);
        }
    }

    // Re-pins the LIVE layout's center dock after a structural change, guarded against the recursion a
    // grafted center dock would cause.
    private void ReinforceLive()
    {
        if (_reinforcing || _workspace.Root is not { } root)
            return;

        _reinforcing = true;
        try
        {
            ReinforceCenter(root);
        }
        finally
        {
            _reinforcing = false;
        }
    }

    // Re-establishes the one invariant that keeps the main window dockable: the center dock — and the
    // chain of docks above it — never collapses, even when everything else was dragged out. Dock's
    // IsEmpty rolls up recursively ("all children empty"), so an empty pinned center inside a
    // collapsable parent still zero-sizes with it, leaving the window with no drop surface at all.
    // Every pin is recomputed from scratch: docks that are no longer on the center's ancestor chain
    // revert to collapsable (no immortal husks after drags reparent things). Runs after every layout
    // restore AND after every structural change (dock/undock/float), because those reparent the
    // center and change which ancestors need pinning.
    private void ReinforceCenter(IRootDock root)
    {
        ResetCollapsePins(root);

        var center = _workspace.EnsureCenterDock(root);
        for (var ancestor = center.Owner; ancestor is IDock parent; ancestor = parent.Owner)
            parent.IsCollapsable = false;
        root.IsCollapsable = false;
    }

    private static void ResetCollapsePins(IDock dock)
    {
        if (dock.VisibleDockables is not { } dockables)
            return;

        foreach (var child in dockables)
        {
            if (child is not IDock childDock || child is IProportionalDockSplitter)
                continue;

            childDock.IsCollapsable = true;
            ResetCollapsePins(childDock);
        }
    }

    // A root-slot side column (Left or Right): its tool dock plus the recursive splits of everything
    // parented into it; null when no dockable claims the slot.
    private IDock? BuildSideColumn(DockSlot slot)
    {
        var items = RootItems(slot);
        if (items.Count == 0)
            return null;

        return WrapWithChildren(NewToolDock(slot + "Dock", AlignmentFor(slot), items), items);
    }

    // The center column: the Top row over the Bottom row, sized to whatever Left/Right leave behind.
    // The top tool dock is the window's home dock area: it exists even with nothing to seed it and never
    // collapses when its last panel floats away — otherwise the window is left with no dock to drop
    // panels back into.
    private IProportionalDock BuildCenter()
    {
        var topItems = RootItems(DockSlot.Top);
        var topDock = NewToolDock(DockSlot.Top + "Dock", Alignment.Top, topItems);
        topDock.IsCollapsable = false;

        var rows = new List<IDock> { WrapWithChildren(topDock, topItems) };
        var bottomItems = RootItems(DockSlot.Bottom);
        if (bottomItems.Count > 0)
        {
            rows.Add(WrapWithChildren(
                NewToolDock(DockSlot.Bottom + "Dock", Alignment.Bottom, bottomItems), bottomItems));
        }

        var children = new List<IDockable>();
        AddWithSplitters(children, rows.ToArray());

        var center = _factory.CreateProportionalDock();
        center.Id = "CenterLayout";
        center.Orientation = Orientation.Vertical;
        var remainder = 1.0 - SlotWidth(DockSlot.Left) - SlotWidth(DockSlot.Right);
        if (remainder > 0)
            center.Proportion = remainder;
        center.VisibleDockables = _factory.CreateList(children.ToArray());
        center.ActiveDockable = rows[0];
        return center;
    }

    // A tool dock holding the given dockables as tabs, sized by the first one's proportion.
    private IToolDock NewToolDock(string id, Alignment alignment, IReadOnlyList<DockableDescriptor> items)
    {
        var tools = items.Select(_workspace.CreateTool).Cast<IDockable>().ToArray();
        var dock = _factory.CreateToolDock();
        dock.Id = id;
        dock.Alignment = alignment;
        if (items.Count > 0 && !double.IsNaN(items[0].Proportion))
            dock.Proportion = items[0].Proportion;
        dock.VisibleDockables = _factory.CreateList(tools);
        dock.ActiveDockable = tools.FirstOrDefault();
        return dock;
    }

    // Recursively splits the dockables parented into any of this dock's members off the dock: each
    // (parent, slot) group becomes its own tool dock — itself wrapped with ITS children — attached on
    // the declared edge. Children attach around the dock CONTAINING the parent, so tabbed siblings
    // share their splits. Dock ids follow the "<parentKey><slot>Dock" convention the Workspace's
    // home-dock lookup mirrors.
    private IDock WrapWithChildren(IDock dock, IReadOnlyList<DockableDescriptor> members)
    {
        var current = dock;
        foreach (var member in members)
        {
            var groups = _workspace.All
                .Where(child => ReferenceEquals(child.Parent, member) && child.Slot != DockSlot.Float)
                .GroupBy(child => child.Slot);
            foreach (var group in groups)
            {
                var items = group.ToList();
                var childDock = NewToolDock(member.Key + group.Key + "Dock", AlignmentFor(group.Key), items);
                current = Split(current, WrapWithChildren(childDock, items), group.Key);
            }
        }

        return current;
    }

    // Wraps host and child in a proportional dock on the child's edge. The host's proportion (its share
    // of the OUTER layout) moves up onto the wrapper; inside, the child keeps its declared share and the
    // host auto-sizes to the rest.
    private IProportionalDock Split(IDock host, IDock child, DockSlot edge)
    {
        var wrapper = _factory.CreateProportionalDock();
        wrapper.Id = child.Id + "Host";
        wrapper.Orientation =
            edge is DockSlot.Left or DockSlot.Right ? Orientation.Horizontal : Orientation.Vertical;
        wrapper.Proportion = host.Proportion;
        host.Proportion = double.NaN;

        var first = edge is DockSlot.Left or DockSlot.Top ? child : host;
        var second = ReferenceEquals(first, child) ? host : child;
        wrapper.VisibleDockables = _factory.CreateList<IDockable>(first, new ProportionalDockSplitter(), second);
        wrapper.ActiveDockable = host;
        return wrapper;
    }

    // The dockables docked directly against the main window on the given edge, in catalog order.
    private List<DockableDescriptor> RootItems(DockSlot slot) =>
        _workspace.All.Where(descriptor => descriptor.Parent is null && descriptor.Slot == slot).ToList();

    private static Alignment AlignmentFor(DockSlot slot) => slot switch
    {
        DockSlot.Left => Alignment.Left,
        DockSlot.Right => Alignment.Right,
        DockSlot.Bottom => Alignment.Bottom,
        _ => Alignment.Top,
    };

    // A root slot's share of the window width, for sizing the center to the remainder.
    private double SlotWidth(DockSlot slot)
    {
        var first = RootItems(slot).FirstOrDefault();
        return first is null || double.IsNaN(first.Proportion) ? 0 : first.Proportion;
    }

    private static void AddWithSplitters(List<IDockable> target, params IDock?[] docks)
    {
        foreach (var dock in docks)
        {
            if (dock is null)
                continue;
            if (target.Count > 0)
                target.Add(new ProportionalDockSplitter());
            target.Add(dock);
        }
    }
}
