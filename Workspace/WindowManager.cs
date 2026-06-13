using Avalonia.Controls;
using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Toybox.Studio.Workspace;

/// <summary>
/// The editor's window manager, built on Dock's <see cref="Factory"/>. It assembles the dock layout from the
/// <see cref="DockableCatalog"/> instead of hard-coded XAML, owns the float / focus / ensure-open logic for
/// opening a dockable on demand, and rehydrates a deserialized layout: <see cref="AttachContent"/> re-binds
/// each tool to its deferred view template (the serializer persists structure and ids but never the live
/// content). It subclasses <see cref="Factory"/> because <c>DockControl</c> needs an <c>IFactory</c>.
/// </summary>
public sealed class WindowManager : Factory
{
    private readonly IReadOnlyList<DockableDescriptor> _dockables;
    private readonly Dictionary<string, DockableDescriptor> _byId;

    public WindowManager(DockableCatalog catalog)
    {
        _dockables = catalog.Dockables;
        _byId = _dockables.ToDictionary(descriptor => descriptor.Id);
    }

    public override IRootDock CreateLayout() => CreateDefaultLayout();

    /// <summary>
    /// Assembles the default three-region layout — Left | [CenterTop / CenterBottom] | Right — from the
    /// catalog's slot/proportion metadata. <see cref="DockSlot.Float"/> dockables are intentionally absent;
    /// they open on demand, docked into the layout. The returned graph still needs <c>InitLayout</c>.
    /// </summary>
    public IRootDock CreateDefaultLayout()
    {
        var children = new List<IDockable>();
        AddWithSplitters(children, BuildToolDock(DockSlot.Left, Alignment.Left), BuildCenter(),
            BuildToolDock(DockSlot.Right, Alignment.Right));

        var main = CreateProportionalDock();
        main.Id = "MainLayout";
        main.Orientation = Orientation.Horizontal;
        main.VisibleDockables = CreateList(children.ToArray());
        main.ActiveDockable = children.FirstOrDefault(child => child is IDock and not IProportionalDockSplitter);

        var root = CreateRootDock();
        root.Id = "Root";
        root.IsCollapsable = false;
        root.VisibleDockables = CreateList<IDockable>(main);
        root.DefaultDockable = main;
        root.ActiveDockable = main;
        return root;
    }

    /// <summary>
    /// Walks a layout (including floating windows) and re-binds every known tool to its deferred view
    /// template. Run after both building the default layout and deserializing a saved one.
    /// </summary>
    public void AttachContent(IDockable? dockable)
    {
        if (dockable is null)
            return;

        if (dockable is Tool tool && _byId.TryGetValue(tool.Id, out var descriptor))
            tool.Content = DeferredContent(descriptor);

        if (dockable is IDock dock && dock.VisibleDockables is { } children)
        {
            foreach (var child in children)
                AttachContent(child);
        }

        if (dockable is IRootDock root && root.Windows is { } windows)
        {
            foreach (var window in windows)
                AttachContent(window.Layout);
        }
    }

    /// <summary>Focuses the dockable if it already exists anywhere in the layout; otherwise docks it open.</summary>
    public void OpenOrFocus(DockableDescriptor descriptor, IRootDock root, Window owner)
    {
        if (TryFindExisting(descriptor.Id, root, out var existing, out var floatingHost))
            Focus(root, existing, floatingHost);
        else
            Open(descriptor, root);
    }

    /// <summary>Docks the dockable open only when it isn't already open anywhere (used by the Play button).</summary>
    public void EnsureOpen(DockableDescriptor descriptor, IRootDock root, Window owner)
    {
        if (!TryFindExisting(descriptor.Id, root, out _, out _))
            Open(descriptor, root);
    }

    public bool IsDocked(string id, IRootDock root) =>
        FindInDock(root, id, out _);

    public bool IsFloating(string id, IRootDock root)
    {
        if (root.Windows is not { } windows)
            return false;

        return windows.Any(window => window.Layout is { } layout && FindInDock(layout, id, out _));
    }

    private IToolDock? BuildToolDock(DockSlot slot, Alignment alignment)
    {
        var items = _dockables.Where(descriptor => descriptor.Slot == slot).ToList();
        if (items.Count == 0)
            return null;

        var tools = items.Select(CreateToolFor).Cast<IDockable>().ToArray();
        var dock = CreateToolDock();
        dock.Id = slot + "Dock";
        dock.Alignment = alignment;
        if (!double.IsNaN(items[0].Proportion))
            dock.Proportion = items[0].Proportion;
        dock.VisibleDockables = CreateList(tools);
        dock.ActiveDockable = tools[0];
        return dock;
    }

    /// <summary>The center column: CenterTop over CenterBottom, sized to whatever Left/Right leave behind.</summary>
    private IProportionalDock? BuildCenter()
    {
        var rows = new List<IDock>();
        if (BuildToolDock(DockSlot.CenterTop, Alignment.Top) is { } top)
            rows.Add(top);
        if (BuildToolDock(DockSlot.CenterBottom, Alignment.Bottom) is { } bottom)
            rows.Add(bottom);
        if (rows.Count == 0)
            return null;

        var children = new List<IDockable>();
        AddWithSplitters(children, rows.ToArray());

        var center = CreateProportionalDock();
        center.Id = "CenterLayout";
        center.Orientation = Orientation.Vertical;
        var remainder = 1.0 - SlotWidth(DockSlot.Left) - SlotWidth(DockSlot.Right);
        if (remainder > 0)
            center.Proportion = remainder;
        center.VisibleDockables = CreateList(children.ToArray());
        center.ActiveDockable = rows[0];
        return center;
    }

    private double SlotWidth(DockSlot slot)
    {
        var first = _dockables.FirstOrDefault(descriptor => descriptor.Slot == slot);
        return first is null || double.IsNaN(first.Proportion) ? 0 : first.Proportion;
    }

    private Tool CreateToolFor(DockableDescriptor descriptor)
    {
        var tool = new Tool { Id = descriptor.Id, Title = descriptor.Title, CanClose = true };
        tool.Content = DeferredContent(descriptor);
        return tool;
    }

    // Hand Dock a deferred-template factory, not a constructed view: Dock rebuilds the content on every
    // dock / theme re-templating. A single live control gets orphaned when re-parented (blanking it); the
    // shared view-model carries all state, so rebuilding the view loses nothing.
    private static Func<IServiceProvider, object> DeferredContent(DockableDescriptor descriptor) =>
        _ => descriptor.CreateView();

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

    // Opens a closed dockable by docking it into the live layout (as a tab) rather than into its own
    // floating window. Programmatically-created Dock host windows aren't wired the way Dock's own drag-float
    // path wires them, and closing one throws inside the library's CloseDockable; docking goes through the
    // same code path as every other panel, so the reopened tool both shows and closes cleanly. The panel
    // lands in its home slot's dock when that still exists, otherwise in the central area as a sensible
    // fallback (covers Float-slot panels like Settings, which have no home dock).
    private void Open(DockableDescriptor descriptor, IRootDock root)
    {
        var target = FindToolDock(root, descriptor.Slot + "Dock")
                     ?? FindToolDock(root, DockSlot.CenterTop + "Dock")
                     ?? FirstToolDock(root);
        if (target is null)
            return;

        var tool = CreateToolFor(descriptor);
        AddDockable(target, tool);
        SetActiveDockable(tool);
        SetFocusedDockable(root, tool);
    }

    private IToolDock? FindToolDock(IDock dock, string id)
    {
        if (dock is IToolDock toolDock && dock.Id == id)
            return toolDock;

        if (dock.VisibleDockables is { } dockables)
        {
            foreach (var child in dockables)
            {
                if (child is IDock childDock && FindToolDock(childDock, id) is { } match)
                    return match;
            }
        }

        return null;
    }

    private IToolDock? FirstToolDock(IDock dock)
    {
        if (dock is IToolDock toolDock)
            return toolDock;

        if (dock.VisibleDockables is { } dockables)
        {
            foreach (var child in dockables)
            {
                if (child is IDock childDock && FirstToolDock(childDock) is { } match)
                    return match;
            }
        }

        return null;
    }

    private void Focus(IRootDock root, IDockable target, Window? floatingHost)
    {
        SetActiveDockable(target);
        if (floatingHost is not null)
            floatingHost.Activate();
        else
            SetFocusedDockable(root, target);
    }

    private bool TryFindExisting(string id, IRootDock root, out IDockable found, out Window? floatingHost)
    {
        if (FindInDock(root, id, out found))
        {
            floatingHost = null;
            return true;
        }

        if (root.Windows is { } windows)
        {
            foreach (var window in windows)
            {
                if (window.Layout is { } layout && FindInDock(layout, id, out found))
                {
                    floatingHost = window.Host as Window;
                    return true;
                }
            }
        }

        found = null!;
        floatingHost = null;
        return false;
    }

    private bool FindInDock(IDock dock, string id, out IDockable found)
    {
        if (dock.VisibleDockables is { } dockables)
        {
            foreach (var dockable in dockables)
            {
                if (dockable.Id == id)
                {
                    found = dockable;
                    return true;
                }

                if (dockable is IDock child && FindInDock(child, id, out found))
                    return true;
            }
        }

        found = null!;
        return false;
    }
}
