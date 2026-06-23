using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Dock.Avalonia.Controls;
using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Shell.Panels;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Shell.Workspace;

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

    // Live title bindings keyed by tool id: each ties a DataPanel's TitleChanged to its dock tab's Title so
    // the '*' tracks dirty state. Held so re-templating / layout restore doesn't double-subscribe and closing
    // a tool can unsubscribe (a stale handler would keep an abandoned Tool alive).
    private readonly Dictionary<string, (DataPanel Owner, Action Handler)> _titleBindings = [];

    // Tool ids whose close we've already cleared through the unsaved-changes prompt: the first close attempt is
    // vetoed to show the prompt, then we re-close, and this lets that second pass through.
    private readonly HashSet<string> _forceClosing = [];

    // The id of the most recently focused tool, tracked so File ▸ Save can resolve "whatever is focused".
    private string? _focusedToolId;

    public WindowManager(DockableCatalog catalog)
    {
        _dockables = catalog.Dockables;
        _byId = _dockables.ToDictionary(descriptor => descriptor.Id);

        // Without a host-window factory Dock has nothing to put a dragged-out panel into, so floating it
        // just makes it vanish. Hand it Dock's themed HostWindow (DockFluentTheme styles it) as the default
        // factory for floated windows. (HostWindowLocator is a per-id dictionary for overrides; the
        // drag-float path falls back to DefaultHostWindowLocator.)
        DefaultHostWindowLocator = () => new HostWindow();

        // Stamp capabilities on every dockable the moment Dock initializes it — at startup (InitLayout) and
        // at runtime (a float/drag/split has the library create fresh root/tool docks we never build or
        // AttachContent over). Without this their Fluent chrome binds into a null DockCapabilityPolicy /
        // DockCapabilityOverrides and floods the log with "Value is null" binding errors. See
        // EnsureDockCapabilities: the instances are empty (all-null), so capability resolution is unchanged.
        DockableInit += (_, args) =>
        {
            if (args.Dockable is { } dockable)
                EnsureDockCapabilities(dockable);
        };

        // Closing a spawned tool's window must dispose its view-model so the engine view + frame stream
        // it owns are torn down.
        DockableClosed += (_, args) => DisposeInstance(args.Dockable);

        // A document with unsaved edits prompts before it closes (see OnDockableClosing).
        DockableClosing += OnDockableClosing;

        // Remember the focused tool so File ▸ Save can target it.
        FocusedDockableChanged += (_, args) =>
        {
            if (args.Dockable is Tool tool)
                _focusedToolId = tool.Id;
        };
    }

    /// <summary>The live view-model of the most recently focused tool, or null. Used by File ▸ Save to save
    /// "whatever is focused".</summary>
    public object? FocusedViewModel() =>
        _focusedToolId is { } id ? ResolveLiveViewModel(id) : null;

    /// <summary>Every open data panel with a live, title-bound view-model, distinct. Used by File ▸ Save All
    /// and the consolidated app-close prompt. Does not instantiate any not-yet-shown view-model.</summary>
    public IEnumerable<DataPanel> OpenPanels() =>
        _titleBindings.Values.Select(binding => binding.Owner).Distinct();

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
        {
            var viewModel = ResolveViewModelForTool(tool.Id, descriptor);
            tool.Content = DeferredContent(descriptor, viewModel);
            BindTitle(tool, viewModel);
            BindToolbar(tool, viewModel);
        }

        if (dockable is IDock dock && dock.VisibleDockables is { } children)
        {
            foreach (var child in children)
                AttachContent(child);
        }

        if (dockable is IRootDock root)
        {
            if (root.Windows is { } windows)
            {
                foreach (var window in windows)
                    AttachContent(window.Layout);
            }

            // Pinned and hidden tools live in the root's own collections, NOT in any dock's
            // VisibleDockables, so the recursion above skips them. Without this their deferred view
            // template is never re-bound on restore and they reveal blank (e.g. a console restored
            // pinned shows no output). Re-bind them too.
            foreach (var pinned in PinnedAndHidden(root))
                AttachContent(pinned);
        }
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

    // The live view-model behind a tool id: a tracked spawned instance, a title-bound owner, or (for an
    // already-shown singleton) its DI instance. Never starts a not-yet-shown singleton's view-model.
    private object? ResolveLiveViewModel(string toolId)
    {
        if (_instances.TryGetValue(toolId, out var instance))
            return instance;
        if (_titleBindings.TryGetValue(toolId, out var binding))
            return binding.Owner;
        if (TryResolveDescriptor(toolId, out var descriptor) && descriptor.Singleton)
            return descriptor.CreateViewModel();
        return null;
    }

    // Intercepts a dockable close: a buffered data panel with unsaved edits (e.g. Settings) vetoes the close
    // and asks Save / Don't Save / Cancel, then re-closes (or stays open on Cancel). Live panels (the world
    // viewport) and clean panels close immediately.
    private void OnDockableClosing(object? sender, DockableClosingEventArgs args)
    {
        if (args.Dockable is not Tool tool)
            return;

        // Second pass after the prompt — let it through.
        if (_forceClosing.Remove(tool.Id))
            return;

        if (!_titleBindings.TryGetValue(tool.Id, out var binding) || !binding.Owner.HasUnsavedChanges)
            return;

        args.Cancel = true;
        PromptThenCloseAsync(tool, binding.Owner).FireAndForget();
    }

    private async Task PromptThenCloseAsync(Tool tool, DataPanel panel)
    {
        var choice = await Popups.ShowSaveChangesAsync([panel.BaseTitle]).ContinueOnSameContext();
        if (choice == SaveChoice.Cancel)
            return; // Keep the tab open.

        if (choice == SaveChoice.Save)
            await panel.SaveAsync().ContinueOnSameContext();
        else
            panel.Cancel(); // Discard the buffered edits.

        _forceClosing.Add(tool.Id);
        CloseDockable(tool);
    }

    // The root's off-layout dockables: the four edge pin strips plus the hidden set. Null-safe — any of
    // these collections may be unset on a freshly built or partially deserialized layout.
    private static IEnumerable<IDockable> PinnedAndHidden(IRootDock root)
    {
        IList<IDockable>?[] collections =
        [
            root.LeftPinnedDockables,
            root.RightPinnedDockables,
            root.TopPinnedDockables,
            root.BottomPinnedDockables,
            root.HiddenDockables,
        ];

        foreach (var collection in collections)
        {
            if (collection is null)
                continue;
            foreach (var dockable in collection)
                yield return dockable;
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
            var tool = NewTool(descriptor, descriptor.Id);
            // A title-owning (DataPanel) singleton is resolved eagerly so its tab title can track dirty state;
            // every other singleton stays lazily resolved by the deferred template (null), so panels with side
            // effects on construction — e.g. the game view's frame stream — aren't started before they show.
            var viewModel = OwnsTitle(descriptor) ? descriptor.CreateViewModel() : null;
            tool.Content = DeferredContent(descriptor, viewModel);
            BindTitle(tool, viewModel);
            BindToolbar(tool, viewModel);
            EnsureDockCapabilities(tool);
            return tool;
        }

        return CreateInstanceTool(descriptor, NextInstanceId(descriptor), descriptor.CreateViewModel());
    }

    // A fresh dock tool for a descriptor: a ToolbarTool (carrying a persisted ToolbarLayout) when the
    // dockable's view-model hosts a toolbar, otherwise a plain Tool.
    private static Tool NewTool(DockableDescriptor descriptor, string id)
    {
        Tool tool = typeof(IToolbarHost).IsAssignableFrom(descriptor.ViewModelType)
            ? new ToolbarTool()
            : new Tool();
        tool.Id = id;
        tool.Title = descriptor.Title;
        tool.CanClose = true;
        return tool;
    }

    // Hands an IToolbarHost view-model the persisted ToolbarLayout its dock tool carries, so toolbar edits
    // mutate the serialized layout in place. Idempotent: BindToolbar no-ops when re-bound to the same layout.
    private static void BindToolbar(Tool tool, object? viewModel)
    {
        if (tool is ToolbarTool toolbarTool && viewModel is IToolbarHost host)
            host.BindToolbar(toolbarTool.Toolbar);
    }

    // Builds a spawned tool bound to a specific view-model, registering it so the deferred template
    // reuses the same instance and the close handler can dispose it.
    private Tool CreateInstanceTool(DockableDescriptor descriptor, string toolId, object viewModel)
    {
        _instances[toolId] = viewModel;
        var tool = NewTool(descriptor, toolId);
        tool.Content = DeferredContent(descriptor, viewModel);
        BindTitle(tool, viewModel);
        BindToolbar(tool, viewModel);
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

    // Whether this dockable's view-model owns its title (a DataPanel that shows a '*'): the cue for the
    // window manager to resolve it eagerly and mirror its title onto the tab.
    private static bool OwnsTitle(DockableDescriptor descriptor) =>
        typeof(DataPanel).IsAssignableFrom(descriptor.ViewModelType);

    // The view-model a tool's title should bind to (and its view should share): the tracked instance for a
    // non-singleton, the DI singleton for a title-owning singleton, else null (the deferred template resolves
    // a plain singleton lazily — see CreateToolFor).
    private object? ResolveViewModelForTool(string toolId, DockableDescriptor descriptor)
    {
        if (!descriptor.Singleton)
            return ResolveInstance(toolId, descriptor);

        return OwnsTitle(descriptor) ? descriptor.CreateViewModel() : null;
    }

    // Mirrors a DataPanel's Title onto its dock tab, now and on every change. Idempotent across the repeated
    // AttachContent passes a layout restore / re-templating triggers: re-binding the same owner just re-derives
    // the title (a serialized Title may carry a stale '*'); a different owner replaces the old subscription.
    private void BindTitle(Tool tool, object? viewModel)
    {
        if (viewModel is not DataPanel owner)
            return;

        if (_titleBindings.TryGetValue(tool.Id, out var existing))
        {
            if (ReferenceEquals(existing.Owner, owner))
            {
                tool.Title = owner.Title;
                return;
            }

            existing.Owner.TitleChanged -= existing.Handler;
        }

        void Handler() => Dispatch.To(DispatchContext.UI, () => tool.Title = owner.Title);
        owner.TitleChanged += Handler;
        _titleBindings[tool.Id] = (owner, Handler);
        tool.Title = owner.Title;
    }

    private void UnbindTitle(string toolId)
    {
        if (_titleBindings.Remove(toolId, out var binding))
            binding.Owner.TitleChanged -= binding.Handler;
    }

    private void ClearInstances()
    {
        foreach (var viewModel in _instances.Values)
            (viewModel as IDisposable)?.Dispose();

        _instances.Clear();
        _instanceCounter = 0;

        // Drop every title binding so a rebuilt layout's fresh tools subscribe cleanly (the old Tool objects
        // are abandoned; leaving their handlers subscribed would keep them alive and update dead tabs).
        foreach (var toolId in _titleBindings.Keys.ToList())
            UnbindTitle(toolId);
    }

    private void DisposeInstance(IDockable? dockable)
    {
        if (dockable is null)
            return;

        // Unbind first: a closed tool (singleton or spawned) must release its title subscription even though
        // only spawned instances carry a disposable view-model.
        UnbindTitle(dockable.Id);

        if (_instances.Remove(dockable.Id, out var viewModel))
            (viewModel as IDisposable)?.Dispose();
    }
}
