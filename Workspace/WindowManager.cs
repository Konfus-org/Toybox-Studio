using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Dock.Avalonia.Controls;
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
    // Separates a non-singleton tool's instance id from its descriptor's base id, e.g. "Viewport#3".
    private const char InstanceSeparator = '#';

    private readonly IReadOnlyList<DockableDescriptor> _dockables;
    private readonly Dictionary<string, DockableDescriptor> _byId;

    // Live view-models for spawned (non-singleton) tools, keyed by unique tool id. Each is created on
    // open and disposed when its window closes; the deferred view template binds to it across
    // re-templating so Dock never resolves a second view-model for the same window.
    private readonly Dictionary<string, object> _instances = [];
    private int _instanceCounter;

    public WindowManager(DockableCatalog catalog)
    {
        _dockables = catalog.Dockables;
        _byId = _dockables.ToDictionary(descriptor => descriptor.Id);

        // Without a host-window factory Dock has nothing to put a dragged-out panel into, so floating it
        // just makes it vanish. Hand it Dock's themed HostWindow (DockFluentTheme styles it) as the default
        // factory for floated windows. (HostWindowLocator is a per-id dictionary for overrides; the
        // drag-float path falls back to DefaultHostWindowLocator.)
        DefaultHostWindowLocator = () => new HostWindow();

        // Closing a spawned tool's window must dispose its view-model so the engine view + frame stream
        // it owns are torn down.
        DockableClosed += (_, args) => DisposeInstance(args.Dockable);
    }

    public override IRootDock CreateLayout() => CreateDefaultLayout();

    /// <summary>
    /// Assembles the default three-region layout — Left | [CenterTop / CenterBottom] | Right — from the
    /// catalog's slot/proportion metadata. <see cref="DockSlot.Float"/> dockables are intentionally absent;
    /// they open on demand, docked into the layout. The returned graph still needs <c>InitLayout</c>.
    /// </summary>
    public IRootDock CreateDefaultLayout()
    {
        // Building a fresh default layout abandons any previously spawned tools, so dispose their
        // view-models and reset the instance counter before re-seeding from the catalog.
        ClearInstances();

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

        EnsureDockCapabilities(dockable);

        if (dockable is Tool tool && TryResolveDescriptor(tool.Id, out var descriptor))
            tool.Content = DeferredContent(descriptor, ResolveInstance(tool.Id, descriptor));

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

    // Dock's Fluent chrome binds straight into each dockable's capability override (and each dock's policy).
    // Those objects are null by design — null means "inherit" — but a null trips a binding error for every
    // panel each time its chrome is templated, which floods the log. Hand each dockable an empty instance:
    // all fields stay null, so capability resolution is unchanged, but the theme now has something to bind.
    private static void EnsureDockCapabilities(IDockable dockable)
    {
        dockable.DockCapabilityOverrides ??= new DockCapabilityOverrides();
        if (dockable is IDock dock)
            dock.DockCapabilityPolicy ??= new DockCapabilityPolicy();
        if (dockable is IRootDock root)
            root.RootDockCapabilityPolicy ??= new DockCapabilityPolicy();
    }

    /// <summary>
    /// For a singleton dockable: focuses it if it already exists anywhere, otherwise docks it open.
    /// For a non-singleton dockable: always docks a fresh instance with its own view-model.
    /// </summary>
    public void OpenOrFocus(DockableDescriptor descriptor, IRootDock root, Window owner)
    {
        if (descriptor.Singleton && TryFindExisting(descriptor.Id, root, out var existing, out var floatingHost))
            Focus(root, existing, floatingHost);
        else
            Open(descriptor, root);
    }

    /// <summary>
    /// Surfaces the dockable (used to bring up the game view on Play): focuses it if open, otherwise
    /// docks it. Same behavior as <see cref="OpenOrFocus"/> — kept as a named entry point for the
    /// Play flow.
    /// </summary>
    public void EnsureOpen(DockableDescriptor descriptor, IRootDock root, Window owner) =>
        OpenOrFocus(descriptor, root, owner);

    // State queries match by base id so a non-singleton with any open instance (e.g. "Viewport#2")
    // reads as docked/floating for its descriptor id ("Viewport").
    public bool IsDocked(string id, IRootDock root) =>
        ContainsBaseId(root, id);

    public bool IsFloating(string id, IRootDock root)
    {
        if (root.Windows is not { } windows)
            return false;

        return windows.Any(window => window.Layout is { } layout && ContainsBaseId(layout, id));
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

    // Builds the tool for a descriptor. Singletons use the descriptor id and bind their DI view-model;
    // non-singletons get a unique instance id and a freshly-spawned, registered view-model.
    private Tool CreateToolFor(DockableDescriptor descriptor)
    {
        if (descriptor.Singleton)
        {
            var tool = new Tool { Id = descriptor.Id, Title = descriptor.Title, CanClose = true };
            tool.Content = DeferredContent(descriptor, null);
            EnsureDockCapabilities(tool);
            return tool;
        }

        return CreateInstanceTool(descriptor, NextInstanceId(descriptor), descriptor.CreateViewModel());
    }

    // Builds a spawned tool bound to a specific view-model, registering it so the deferred template
    // reuses the same instance and the close handler can dispose it.
    private Tool CreateInstanceTool(DockableDescriptor descriptor, string toolId, object viewModel)
    {
        _instances[toolId] = viewModel;
        var tool = new Tool { Id = toolId, Title = descriptor.Title, CanClose = true };
        tool.Content = DeferredContent(descriptor, viewModel);
        EnsureDockCapabilities(tool);
        return tool;
    }

    private string NextInstanceId(DockableDescriptor descriptor) =>
        $"{descriptor.Id}{InstanceSeparator}{++_instanceCounter}";

    // Hand Dock a deferred-template factory, not a constructed view: Dock rebuilds the content on every
    // dock / theme re-templating. A single live control gets orphaned when re-parented (blanking it); the
    // view-model carries all state, so rebuilding the view loses nothing. A null view-model means "resolve
    // from DI" (singletons); a spawned instance passes its own view-model so every rebuild reuses it.
    private static Func<IServiceProvider, object> DeferredContent(DockableDescriptor descriptor, object? viewModel) =>
        _ => descriptor.CreateView(viewModel);

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

    // The descriptor id for a tool id: identical for singletons, the part before '#' for spawned tools.
    private static string BaseId(string toolId)
    {
        var separator = toolId.IndexOf(InstanceSeparator);
        return separator < 0 ? toolId : toolId[..separator];
    }

    private bool TryResolveDescriptor(
        string toolId,
        [MaybeNullWhen(false)] out DockableDescriptor descriptor) =>
        _byId.TryGetValue(BaseId(toolId), out descriptor);

    // The view-model a tool should bind to: null for singletons (resolved from DI by the deferred
    // template); for a spawned tool, its registered instance — created and tracked here on first sight
    // so layout restore rehydrates every saved viewport, idempotently across AttachContent re-runs.
    private object? ResolveInstance(string toolId, DockableDescriptor descriptor)
    {
        if (descriptor.Singleton)
            return null;

        if (!_instances.TryGetValue(toolId, out var viewModel))
        {
            viewModel = descriptor.CreateViewModel();
            _instances[toolId] = viewModel;
            TrackRestoredInstanceId(toolId);
        }

        return viewModel;
    }

    // Keep the spawn counter ahead of any restored instance id so later opens never collide with one.
    private void TrackRestoredInstanceId(string toolId)
    {
        var separator = toolId.IndexOf(InstanceSeparator);
        if (separator >= 0
            && int.TryParse(toolId.AsSpan(separator + 1), out var suffix)
            && suffix > _instanceCounter)
            _instanceCounter = suffix;
    }

    // Whether any dockable under this dock (recursively) belongs to the given descriptor id.
    private static bool ContainsBaseId(IDock dock, string id)
    {
        if (dock.VisibleDockables is not { } dockables)
            return false;

        foreach (var dockable in dockables)
        {
            if (BaseId(dockable.Id) == id)
                return true;
            if (dockable is IDock child && ContainsBaseId(child, id))
                return true;
        }

        return false;
    }

    private void ClearInstances()
    {
        foreach (var viewModel in _instances.Values)
            (viewModel as IDisposable)?.Dispose();

        _instances.Clear();
        _instanceCounter = 0;
    }

    private void DisposeInstance(IDockable? dockable)
    {
        if (dockable is null || !_instances.Remove(dockable.Id, out var viewModel))
            return;

        (viewModel as IDisposable)?.Dispose();
    }
}
