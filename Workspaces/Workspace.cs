using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using Toybox.Studio.AssetOwners;
using Toybox.Studio.Dialogs;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Attributes;
using Toybox.Studio.Utils.Toolbars;

namespace Toybox.Studio.Workspaces;

/// <summary>
/// The workspace: owns the registered dockables and every open panel, dealing purely in windows.
/// Query state through <see cref="All"/> / <see cref="Opened"/> / <see cref="Docked"/> /
/// <see cref="Floating"/> / <see cref="Closed"/> and <see cref="Find(string)"/>; act through
/// <see cref="Open"/> / <see cref="OpenOrFocus"/> / <see cref="Focus"/> / <see cref="Close"/> — all
/// against the live layout (<see cref="Root"/>), driving Dock through the shared
/// <see cref="DockFactory"/> it holds privately. Runtime behavior rides on the factory's events:
/// spawned view-models are disposed when their panels close, an <see cref="AssetOwnerViewModel"/>'s
/// dirty-star title mirrors onto its dock tab via the <see cref="OwnerTabBinder"/> (prompting
/// Save / Don't Save / Cancel before a dirty panel closes), the <see cref="IDockAware"/> open/close
/// hooks fire (e.g. Settings' draft lifecycle), and emptied floating windows are swept away. Building,
/// healing, and persisting whole layouts is <see cref="LayoutManager"/>'s business; layout-tree
/// searches live in <see cref="DockTree"/>.
/// </summary>
public sealed class Workspace
{
    // Separates a non-singleton tool's instance id from its descriptor's base id, e.g. "ViewportViewModel#3".
    private const char InstanceSeparator = '#';

    private readonly Popups _popups;
    private readonly DockFactory _factory;

    // The dockable registry, keyed for the two lookups that matter: persisted tool id and view-model type.
    private readonly Dictionary<string, DockableDescriptor> _byKey;
    private readonly Dictionary<Type, DockableDescriptor> _byType;

    // The live view-model of every open panel, keyed by tool id (a singleton's id is its descriptor
    // key; a spawned instance's carries the '#N' suffix). Each is created through the descriptor's
    // factory when its tool materializes and dropped when the panel closes (spawned ones disposed);
    // the deferred view template binds to it across re-templating, so a panel never sees a second
    // view-model mid-life.
    private readonly Dictionary<string, object> _instances = [];

    // Mints unique '#N' suffixes for spawned (non-singleton) instances.
    private int _instanceCounter;

    // Ties each open AssetOwnerViewModel to its dock tab (dirty-star title + Cancel closes the tab).
    private readonly OwnerTabBinder _owners = new();

    // Tool ids whose IDockAware view-model has been told it opened, so the repeated attach passes a
    // re-template triggers call OnDockOpened exactly once per open (and OnDockClosed pairs with it).
    private readonly HashSet<string> _openDockAware = [];

    // Tool ids whose close we've already cleared through the unsaved-changes prompt (or an explicit
    // Cancel): the first close attempt is vetoed to show the prompt, then we re-close, and this lets
    // that second pass through.
    private readonly HashSet<string> _forceClosing = [];

    internal Workspace(IReadOnlyList<DockableDescriptor> dockables, Popups popups, DockFactory factory)
    {
        All = dockables;
        _byKey = dockables.ToDictionary(descriptor => descriptor.Key);
        _byType = dockables.ToDictionary(descriptor => descriptor.ViewModelType);
        _popups = popups;
        _factory = factory;

        // Closing a panel must release its bindings, run its IDockAware close hook, and — for a spawned
        // tool — dispose its view-model so the engine view + frame stream it owns are torn down.
        _factory.DockableClosed += (_, args) => ReleaseClosedDockable(args.Dockable);

        // A panel with unsaved edits prompts before it closes (see OnDockableClosing).
        _factory.DockableClosing += OnDockableClosing;

        // Dragging a floating window only offers drop targets when ITS root has a FocusedDockable —
        // Dock reads the drag source from it and silently bails otherwise. A float restored from a
        // saved layout (or opened programmatically) may carry none, so seed it as the window opens.
        _factory.WindowOpened += (_, args) => FocusFloatContent(args.Window);

        // Dock closes a floating window when the WINDOW itself is drag-docked, but dragging its last
        // TAB back into the layout (or closing it) only empties the window's layout — the host window
        // is left behind as an empty shell. Sweep the floats after any change that can empty one,
        // deferred a tick so the drag/close that emptied the window fully unwinds before its host
        // (often the very control the drag started on) closes under it.
        _factory.DockableDocked += (_, _) => ScheduleEmptyFloatPrune();
        _factory.DockableRemoved += (_, _) => ScheduleEmptyFloatPrune();
        _factory.DockableClosed += (_, _) => ScheduleEmptyFloatPrune();
    }

    /// <summary>The live layout every window operation targets — assigned by the
    /// <see cref="WorkspaceViewModel"/> whenever it swaps the <c>DockControl</c>'s layout. Operations
    /// no-op while none is live.</summary>
    internal IRootDock? Root { get; set; }

    /// <summary>Every registered dockable, in catalog order.</summary>
    public IReadOnlyList<DockableDescriptor> All { get; }

    /// <summary>Dockables with an open instance anywhere — docked or floating.</summary>
    public IEnumerable<DockableDescriptor> Opened =>
        All.Where(descriptor => Find(descriptor.Key) is not null);

    /// <summary>Dockables currently open in the main docked layout.</summary>
    public IEnumerable<DockableDescriptor> Docked =>
        All.Where(descriptor => Find(descriptor.Key, out var window) is not null && window is null);

    /// <summary>Dockables currently open in their own floating window.</summary>
    public IEnumerable<DockableDescriptor> Floating =>
        All.Where(descriptor => Find(descriptor.Key, out var window) is not null && window is not null);

    /// <summary>Registered dockables that are not currently open anywhere.</summary>
    public IEnumerable<DockableDescriptor> Closed =>
        All.Where(descriptor => Find(descriptor.Key) is null);

    /// <summary>
    /// Opens a fresh instance of the dockable with its own view-model — docked into its home area, or
    /// floating at its declared bounds for a <see cref="DockSlot.Float"/> dockable.
    /// </summary>
    public void Open(DockableDescriptor descriptor)
    {
        if (Root is not { } root)
            return;

        if (descriptor.Slot == DockSlot.Float)
            OpenFloating(descriptor, root);
        else
            OpenDocked(descriptor, root);
    }

    /// <summary>For a singleton dockable: focuses the open instance, otherwise opens one. A
    /// non-singleton always opens a fresh instance.</summary>
    public void OpenOrFocus(DockableDescriptor descriptor)
    {
        if (descriptor.Singleton && Focus(descriptor.Key))
            return;

        Open(descriptor);
    }

    /// <summary>Brings an open instance of the dockable to the front — activating its floating window
    /// when it lives in one. Returns false when none is open.</summary>
    public bool Focus(string id)
    {
        if (Root is not { } root || Find(id, out var floatingWindow) is not { } found)
            return false;

        _factory.SetActiveDockable(found);
        if (floatingWindow?.Host is Window host)
            host.Activate();
        else
            _factory.SetFocusedDockable(root, found);
        return true;
    }

    /// <summary>Closes the open instance of the dockable; no-op when none is open. A dirty panel still
    /// gets its unsaved-changes prompt.</summary>
    public void Close(string id)
    {
        if (Find(id) is { } found)
            _factory.CloseDockable(found);
    }

    /// <summary>The open instance of the dockable — a singleton by its key, a spawned non-singleton
    /// ("ViewportViewModel#3") by its base key — or null when none is open.</summary>
    public IDockable? Find(string id) => Find(id, out _);

    /// <summary>As <see cref="Find(string)"/>, also reporting the floating window hosting the instance
    /// — null when it is docked in the main layout.</summary>
    public IDockable? Find(string id, out IDockWindow? floatingWindow)
    {
        floatingWindow = null;
        if (Root is not { } root)
            return null;

        return DockTree.TryFind(root, dockable => BaseId(dockable.Id) == id, out var found, out floatingWindow)
            ? found
            : null;
    }

    /// <summary>The registered dockable backed by the given view-model type, or null.</summary>
    internal DockableDescriptor? ForType(Type viewModelType) => _byType.GetValueOrDefault(viewModelType);

    /// <summary>
    /// Releases every runtime binding, runs the <see cref="IDockAware"/> close hooks, then drops every
    /// open panel's view-model — disposing the spawned (non-singleton) ones so their engine resources
    /// stop — and restarts instance-id minting. Runs before a layout rebuild/restore and at app shutdown.
    /// </summary>
    internal void ResetInstances()
    {
        // Run the close hooks first, while the bindings can still resolve their view-models.
        foreach (var toolId in _openDockAware.ToList())
            NotifyClosed(toolId);
        _openDockAware.Clear();

        _owners.UnbindAll();
        _forceClosing.Clear();

        foreach (var toolId in _instances.Keys.ToList())
            DropInstance(toolId);

        _instanceCounter = 0;
    }

    // Builds the tool for a descriptor, creating its view-model through the descriptor's factory and
    // registering it so the deferred template reuses the same instance and the close handler can drop
    // it. A singleton's tool id is its descriptor key; a non-singleton gets a unique instance id.
    internal Tool CreateTool(DockableDescriptor descriptor)
    {
        var toolId = descriptor.Singleton
            ? descriptor.Key
            : $"{descriptor.Key}{InstanceSeparator}{++_instanceCounter}";
        var viewModel = descriptor.CreateViewModel();
        _instances[toolId] = viewModel;

        var tool = NewTool(descriptor, toolId);
        tool.Content = DeferredContent(descriptor, viewModel);
        WireToolBindings(tool, viewModel);
        DockFactory.EnsureDockCapabilities(tool);
        return tool;
    }

    // Re-binds one tool to its deferred view template and runtime bindings — the per-window leg of a
    // layout rehydration (LayoutManager walks the tree and calls this for every tool it finds).
    // Idempotent across the repeated passes a restore / re-template triggers.
    internal void AttachTool(Tool tool)
    {
        if (!TryResolveDescriptor(tool.Id, out var descriptor))
            return;

        // Re-stamp the header icon: the layout serializer drops it (re-derived from the descriptor), so a
        // restored tool needs it set again before its tab / chrome header binds.
        if (tool is DockPanelRecord record)
            record.IconName = descriptor.Icon;

        var viewModel = ResolveViewModel(tool.Id, descriptor);
        tool.Content = DeferredContent(descriptor, viewModel);
        WireToolBindings(tool, viewModel);
        DockFactory.EnsureDockCapabilities(tool);
    }

    /// <summary>Seeds a floating window's root with a focused dockable when it has none, so dragging
    /// the window over the main window offers drop targets (see the ctor note).</summary>
    internal void FocusFloatContent(IDockWindow? window)
    {
        if (window?.Layout is not IRootDock root || root.FocusedDockable is not null)
            return;

        if (DockTree.TryFind(root, dockable => dockable is Tool, out var tool))
            _factory.SetFocusedDockable(root, tool);
    }

    // The tool dock new windows land in, guaranteed to exist: the center dock when the layout still has
    // one, else any surviving tool dock, else a fresh non-collapsable center dock grafted into the
    // layout — so a window whose panels were all floated away (including one restored from a layout
    // saved that way) always keeps an area to dock back into.
    internal IToolDock EnsureCenterDock(IRootDock root)
    {
        if (DockTree.FindToolDock(root, DockSlot.Top + "Dock") is { } center)
            return KeepAlive(center);
        if (DockTree.FirstToolDock(root) is { } any)
            return KeepAlive(any);

        var dock = _factory.CreateToolDock();
        dock.Id = DockSlot.Top + "Dock";
        dock.Alignment = Alignment.Top;
        dock.VisibleDockables = _factory.CreateList<IDockable>();

        var host = DockTree.FirstProportionalDock(root) ?? (IDock)root;
        host.VisibleDockables ??= _factory.CreateList<IDockable>();
        _factory.AddDockable(host, dock);
        return KeepAlive(dock);
    }

    // The descriptor behind a tool id (matching spawned "Key#N" ids by their base key).
    private bool TryResolveDescriptor(string toolId, [MaybeNullWhen(false)] out DockableDescriptor descriptor) =>
        _byKey.TryGetValue(BaseId(toolId), out descriptor);

    // The descriptor id for a tool id: identical for singletons, the part before '#' for spawned tools.
    private static string BaseId(string toolId)
    {
        var separator = toolId.IndexOf(InstanceSeparator);
        return separator < 0 ? toolId : toolId[..separator];
    }

    // Keeps the spawn counter ahead of a restored instance id so later opens never collide with it.
    private void TrackRestoredInstanceId(string toolId)
    {
        var separator = toolId.IndexOf(InstanceSeparator);
        if (separator >= 0
            && int.TryParse(toolId.AsSpan(separator + 1), out var suffix)
            && suffix > _instanceCounter)
            _instanceCounter = suffix;
    }

    // The guaranteed dock area must hold its ground even when empty: the proportional panel zero-sizes
    // any child whose IsCollapsable && IsEmpty, and a zero-size dock has nothing to hover for the
    // CENTER drop target — only the window-edge targets remain. ONLY the center dock itself is pinned:
    // pinning ancestors by construction leaves immortal empty husks once Dock's drag operations
    // reparent things (an empty pinned dock then hogs its proportion forever); a dock that actually
    // contains the center is never empty, so it never collapses anyway.
    private static IToolDock KeepAlive(IToolDock dock)
    {
        dock.IsCollapsable = false;
        return dock;
    }

    // A fresh dock tool for a descriptor, carrying its header icon. A panel whose view-model hosts an
    // overlay toolbar gets the record subclass that rides the toolbar's placement in the saved layout.
    private static Tool NewTool(DockableDescriptor descriptor, string id)
    {
        DockPanelRecord tool = typeof(IToolbarHost).IsAssignableFrom(descriptor.ViewModelType)
            ? new ToolbarPanelRecord()
            : new DockPanelRecord();
        tool.Id = id;
        tool.Title = descriptor.Title;
        tool.IconName = descriptor.Icon;
        tool.CanClose = true;
        return tool;
    }

    // Hand Dock a deferred-template factory, not a constructed view: Dock rebuilds the content on every
    // dock / theme re-templating. A single live control gets orphaned when re-parented (blanking it); the
    // view-model carries all state, so rebuilding the view loses nothing — every rebuild binds the same
    // tracked view-model.
    private static Func<IServiceProvider, object> DeferredContent(DockableDescriptor descriptor, object viewModel) =>
        _ => descriptor.CreateView(viewModel);

    // The view-model a tool binds to: the tracked instance when the panel is already open, else one
    // created and registered here on first sight — so layout restore rehydrates every saved panel,
    // idempotently across repeated attach passes. (The restored-id tracking keeps the spawned-id counter
    // ahead of ids loaded from a saved layout; it no-ops for singleton ids.)
    private object ResolveViewModel(string toolId, DockableDescriptor descriptor)
    {
        if (!_instances.TryGetValue(toolId, out var viewModel))
        {
            viewModel = descriptor.CreateViewModel();
            _instances[toolId] = viewModel;
            TrackRestoredInstanceId(toolId);
        }

        return viewModel;
    }

    // Wires the runtime bindings for a materialized tool — the owner-to-tab binding (the owner's Cancel
    // button means "discard my edits and close", so its close skips the unsaved-changes prompt), the
    // hosted toolbar's persisted placement, and the IDockAware open hook. Runs whenever a tool
    // (re)binds its deferred content — on creation, on open, and on every attach pass of a restore —
    // so it must stay idempotent.
    private void WireToolBindings(Tool tool, object viewModel)
    {
        _owners.Bind(tool, viewModel, closeTab: () => ForceClose(tool));

        // The layout's persisted toolbar placements flow to the hosting view-model (BindToolbars is
        // idempotent by contract). A plain record for an IToolbarHost — a pre-toolbar saved layout —
        // just leaves the host on its default states until the panel is next opened fresh.
        if (tool is ToolbarPanelRecord record && viewModel is IToolbarHost host)
            host.BindToolbars(record.Toolbars);

        if (viewModel is IDockAware aware && _openDockAware.Add(tool.Id))
            aware.OnDockOpened();
    }

    // Opens a fresh instance by docking it into the live layout (as a tab): its own default dock when
    // the layout still has it, the nearest surviving ancestor's otherwise, else the guaranteed center
    // area (covers a layout whose docks were all floated away).
    private void OpenDocked(DockableDescriptor descriptor, IRootDock root)
    {
        var tool = CreateTool(descriptor);
        _factory.AddDockable(HomeDockFor(descriptor, root), tool);
        _factory.SetActiveDockable(tool);
        _factory.SetFocusedDockable(root, tool);
    }

    // The dock an opened dockable lands in: its own default dock, the nearest surviving ancestor's,
    // else the guaranteed center area.
    private IToolDock HomeDockFor(DockableDescriptor descriptor, IRootDock root)
    {
        for (var target = descriptor; target is not null; target = target.Parent)
        {
            if (target.Slot != DockSlot.Float && DockTree.FindToolDock(root, DockIdFor(target)) is { } dock)
                return dock;
        }

        return EnsureCenterDock(root);
    }

    // The id of the tool dock a descriptor sits in by default (mirrors LayoutManager's default-layout
    // dock naming).
    private static string DockIdFor(DockableDescriptor descriptor) =>
        descriptor.Parent is { } parent ? parent.Key + descriptor.Slot + "Dock" : descriptor.Slot + "Dock";

    // Opens a Float-slot dockable in its own floating window at the descriptor's declared bounds
    // (position defaulting to centered over the main window). The tool is docked first and floated
    // through Dock's own FloatDockable — the same path a drag-out takes, so the host window is wired to
    // close (and dock back) cleanly. The bounds are seeded on the tool (FloatDockable derives the
    // window's placement from them) and re-asserted on the created window.
    private void OpenFloating(DockableDescriptor descriptor, IRootDock root)
    {
        var (x, y, width, height) = descriptor.FloatBounds;
        if (double.IsNaN(x) || double.IsNaN(y))
            (x, y) = OwnerWindow is { } owner ? CenteredOver(owner, width, height) : (0, 0);

        var tool = CreateTool(descriptor);
        _factory.AddDockable(EnsureCenterDock(root), tool);
        tool.SetVisibleBounds(x, y, width, height);
        tool.SetPointerScreenPosition(x, y);
        _factory.FloatDockable(tool);

        if (FindWindowHosting(root, tool) is { } window)
        {
            window.X = x;
            window.Y = y;
            window.Width = width;
            window.Height = height;
        }

        _factory.SetActiveDockable(tool);
    }

    // The window hosting the main DockControl (the first registered control), anchoring the default
    // centered placement of a fresh float.
    private Window? OwnerWindow => _factory.DockControls.OfType<Visual>()
        .Select(visual => TopLevel.GetTopLevel(visual))
        .OfType<Window>()
        .FirstOrDefault();

    // The screen position centering a float of the given size over the owner window. The owner's
    // position is physical pixels while sizes are logical units, so the size converts through the
    // owner's render scaling — matching the screen space Dock places float windows in.
    private static (double X, double Y) CenteredOver(Window owner, double width, double height)
    {
        var scale = owner.RenderScaling;
        var x = owner.Position.X + (owner.ClientSize.Width - width) * scale / 2;
        var y = owner.Position.Y + (owner.ClientSize.Height - height) * scale / 2;
        return (Math.Max(0, x), Math.Max(0, y));
    }

    // The floating window whose layout hosts the given dockable, or null (e.g. the float failed and the
    // tool stayed docked).
    private static IDockWindow? FindWindowHosting(IRootDock root, IDockable dockable)
    {
        if (root.Windows is not { } windows)
            return null;

        return windows.FirstOrDefault(window =>
            window.Layout is { } layout && DockTree.TryFind(layout, other => ReferenceEquals(other, dockable), out _));
    }

    // See the ctor note: runs the empty-float sweep on the next dispatcher tick.
    private void ScheduleEmptyFloatPrune() =>
        Dispatch.To(DispatchContext.UI, PruneEmptyFloats);

    // Closes every floating window whose layout no longer holds a single tool (visible, pinned, or
    // hidden). Idempotent — re-runs triggered by the teardown it causes find nothing left to do.
    private void PruneEmptyFloats()
    {
        // Snapshot: Exit() below closes a float's host window, whose DockControl unregisters itself
        // from DockControls as it detaches — mutating the list mid-walk.
        foreach (var dockControl in _factory.DockControls.ToList())
        {
            if (dockControl.Layout is not IRootDock root || root.Windows is not { } windows)
                continue;

            foreach (var window in windows.ToList())
            {
                if (window.Layout is { } layout
                    && (DockTree.TryFind(layout, dockable => dockable is Tool, out _)
                        || DockTree.PinnedAndHidden(layout).OfType<Tool>().Any()))
                    continue;

                // Exit closes the live host window (whose OnClosed strips it from the model);
                // RemoveWindow covers one that was never presented.
                window.Exit();
                if (windows.Contains(window))
                    _factory.RemoveWindow(window);
            }
        }
    }

    // Closes a dockable, skipping the unsaved-changes prompt: the prompt (or the owner's own Cancel
    // button) has already decided this close.
    private void ForceClose(IDockable dockable)
    {
        _forceClosing.Add(dockable.Id);
        _factory.CloseDockable(dockable);
    }

    // Intercepts a dockable close: a panel with unsaved edits (e.g. Settings) vetoes the close and asks
    // Save / Don't Save / Cancel, then re-closes (or stays open on Cancel). Clean panels close immediately.
    private void OnDockableClosing(object? sender, DockableClosingEventArgs args)
    {
        if (args.Dockable is not Tool tool)
            return;

        // Second pass after the prompt (or an explicit discard) — let it through.
        if (_forceClosing.Remove(tool.Id))
            return;

        if (!_owners.TryGetOwner(tool.Id, out var owner) || !owner.IsDirty)
            return;

        args.Cancel = true;
        PromptThenCloseAsync(tool, owner).FireAndForget();
    }

    private async Task PromptThenCloseAsync(Tool tool, AssetOwnerViewModel owner)
    {
        var name = owner.Title.TrimEnd('*', ' ');
        var choice = await _popups
            .ConfirmAsync<SaveChoice>("Unsaved changes", $"Save changes to {name} before closing?")
            .ContinueOnSameContext();
        if (choice == SaveChoice.Cancel)
            return; // Keep the tab open.

        if (choice == SaveChoice.Save)
            await owner.SaveCommand.ExecuteAsync(null).ContinueOnSameContext();
        // Don't Save just closes: the IDockAware close hook discards the buffered edits.

        ForceClose(tool);
    }

    private void ReleaseClosedDockable(IDockable? dockable)
    {
        if (dockable is null)
            return;

        // Unbind and notify first, while the instance table can still resolve the view-model.
        _owners.Unbind(dockable.Id);

        if (_openDockAware.Remove(dockable.Id))
            NotifyClosed(dockable.Id);

        DropInstance(dockable.Id);
    }

    // Runs the IDockAware close hook on a tool id's live (still-tracked) view-model.
    private void NotifyClosed(string toolId) =>
        (_instances.GetValueOrDefault(toolId) as IDockAware)?.OnDockClosed();

    // Drops a closed panel's view-model from the instance table. Only a spawned (non-singleton) one is
    // disposed — it owns its engine resources; a singleton's instance belongs to its composition-root
    // factory (a closure-held one survives close/reopen).
    private void DropInstance(string toolId)
    {
        if (!_instances.Remove(toolId, out var viewModel))
            return;

        if (TryResolveDescriptor(toolId, out var descriptor) && !descriptor.Singleton)
            (viewModel as IDisposable)?.Dispose();
    }
}
