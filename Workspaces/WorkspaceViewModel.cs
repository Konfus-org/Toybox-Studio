using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Toybox.Studio.Dialogs;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Workspaces;

/// <summary>
/// The shell's workspace face: exposes the registered dockables and their current state
/// (<see cref="All"/> / <see cref="Docked"/> / <see cref="Floating"/> / <see cref="Closed"/>) and
/// drives the <see cref="Workspace"/> (windows: open/focus) and <see cref="LayoutManager"/>
/// (layouts: reset/save/load). Keeping this here keeps the main window's view-model out of the
/// docking weeds.
/// </summary>
public sealed partial class WorkspaceViewModel : ObservableObject
{
    private readonly Workspace _workspace;
    private readonly LayoutManager _layouts;
    private readonly Popups _popups;
    private readonly Logger _log;

    // The one Dock factory instance the managers and the DockControl share — a private detail of the
    // workspace trio, never handed out (see DockFactory).
    private readonly DockFactory _factory;

    private DockControl? _control;

    public WorkspaceViewModel(DockableCatalog catalog, Popups popups, Logger log)
    {
        _popups = popups;
        _log = log;
        _factory = new DockFactory();
        _workspace = new Workspace(catalog.Dockables, popups, _factory);
        _layouts = new LayoutManager(_workspace, _factory, log);
    }

    /// <summary>Every registered dockable. Drives the Window menu.</summary>
    public IReadOnlyList<DockableDescriptor> All => _workspace.All;

    /// <summary>Dockables with an open instance anywhere — docked or floating.</summary>
    public IEnumerable<DockableDescriptor> Opened => _workspace.Opened;

    /// <summary>Dockables currently in the main docked layout.</summary>
    public IEnumerable<DockableDescriptor> Docked => _workspace.Docked;

    /// <summary>Dockables currently in their own floating window.</summary>
    public IEnumerable<DockableDescriptor> Floating => _workspace.Floating;

    /// <summary>Registered dockables that are not currently open anywhere.</summary>
    public IEnumerable<DockableDescriptor> Closed => _workspace.Closed;

    /// <summary>The named layout to restore on launch — the hosting window's working layout, assigned by
    /// its view-model on creation (the workspace itself has no reserved name). It is hidden from the
    /// Load Layout picker; without one, launch goes straight to the built-in default.</summary>
    public string? StartupLayout { get; set; }

    /// <summary>Hooks up the live <see cref="DockControl"/>: assigns the factory, then swaps in the
    /// layout to show on launch — the saved <see cref="StartupLayout"/>, or the built-in default — and
    /// tracks state. The control keeps its inline placeholder layout while the saved one reads from disk.</summary>
    public async Task BindAsync(DockControl control)
    {
        _control = control;
        control.Factory = _factory;
        SwapLayout(await BuildInitialLayoutAsync().ContinueOnSameContext());
        ShowFloatingWindows();
        Refresh();
    }

    /// <summary>Re-announces <see cref="Opened"/> / <see cref="Docked"/> / <see cref="Floating"/> /
    /// <see cref="Closed"/> — the window manager computes them live from the layout; this just tells
    /// bindings to re-read.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Opened));
        OnPropertyChanged(nameof(Docked));
        OnPropertyChanged(nameof(Floating));
        OnPropertyChanged(nameof(Closed));
    }

    /// <summary>Opens (or focuses, if already open) the given dockable. The Window menu binds this,
    /// passing the descriptor it already holds.</summary>
    [RelayCommand]
    public void Open(DockableDescriptor descriptor)
    {
        _workspace.OpenOrFocus(descriptor);
        Refresh();
    }

    /// <summary>Opens (or focuses) the dockable backed by <typeparamref name="TViewModel"/>.</summary>
    public void Open<TViewModel>()
    {
        if (_workspace.ForType(typeof(TViewModel)) is { } descriptor)
            Open(descriptor);
    }

    /// <summary>Brings an already-open instance of the <typeparamref name="TViewModel"/> dockable (by base
    /// key) to the front. Returns false when none is open.</summary>
    public bool Focus<TViewModel>()
    {
        if (_workspace.ForType(typeof(TViewModel)) is not { } descriptor)
            return false;

        var focused = _workspace.Focus(descriptor.Key);
        if (focused)
            Refresh();
        return focused;
    }

    /// <summary>Discards the current arrangement and rebuilds the built-in default layout.</summary>
    [RelayCommand]
    public void ResetLayout()
    {
        if (_control is null)
            return;

        CloseFloatingWindows();
        SwapLayout(_layouts.CreateDefault());
        Refresh();
    }

    /// <summary>Prompts for a name and saves the current arrangement as a named layout the user can restore.</summary>
    [RelayCommand]
    private async Task SaveLayout()
    {
        if (_workspace.Root is not { } root)
            return;

        var name = await _popups
            .ShowAsync(new TextPromptPopupViewModel("Save Layout", "Layout name", confirmText: "Save"))
            .ContinueOnSameContext();
        if (string.IsNullOrWhiteSpace(name))
            return;

        await _layouts.SaveAsync(name, root).ContinueOnSameContext();
        _log.Info($"Saved layout '{name}'.");
    }

    /// <summary>Lets the user pick a saved layout and swaps the live arrangement to it.</summary>
    [RelayCommand]
    private async Task LoadLayout()
    {
        // The working layout is bookkeeping, not one of the user's saved arrangements.
        var names = (await _layouts.ListAsync().ContinueOnSameContext())
            .Where(name => !string.Equals(name, StartupLayout, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (names.Count == 0)
        {
            await _popups.InfoAsync("Load Layout", "No saved layouts yet.").ContinueOnSameContext();
            return;
        }

        var options = names.Select(name => new ListPickItem(name, name, "Saved layout")).ToList();
        var pick = await _popups
            .ShowAsync(new ListPickPopupViewModel("Load Layout", options, confirmText: "Load"))
            .ContinueOnSameContext();
        if (pick is null || _control is null)
            return;

        if (await _layouts.LoadAsync(pick.Key).ContinueOnSameContext() is not { } layout)
        {
            await _popups.ErrorAsync("Load Layout", $"Couldn't load layout '{pick.Key}'.")
                .ContinueOnSameContext();
            return;
        }

        CloseFloatingWindows();

        // A layout that reads but can't be brought live (schema drift mid-tree) has already torn down
        // the open panels, so fall back to the default rather than leaving dead tabs on screen.
        var restored = _layouts.Restore(layout);
        if (!restored)
            layout = _layouts.CreateDefault();

        SwapLayout(layout);
        ShowFloatingWindows();
        Refresh();

        if (!restored)
        {
            await _popups.ErrorAsync("Load Layout", $"Couldn't restore layout '{pick.Key}'.")
                .ContinueOnSameContext();
        }
    }

    /// <summary>Persists the current arrangement under the given name, silently — the programmatic
    /// counterpart of the prompting <see cref="SaveLayoutCommand"/>. The main window saves its working
    /// layout through here as it closes; the layout is captured synchronously (before the returned
    /// task's first await), so the caller may fire-and-forget as long as something later keeps the
    /// process alive for the write (see <c>Launcher.Shutdown</c>).</summary>
    public Task SaveLayoutAsync(string name) =>
        _workspace.Root is { } root ? _layouts.SaveAsync(name, root) : Task.CompletedTask;

    /// <summary>Disposes every spawned panel view-model (stopping its engine view). Called on app exit,
    /// after the closing window saved its layout and before the engine connection tears down.</summary>
    public void DisposeInstances() => _workspace.ResetInstances();

    private async Task<IRootDock> BuildInitialLayoutAsync()
    {
        // A saved working layout that reads and restores cleanly wins; anything else (missing file,
        // schema drift, partial write) falls back to the built-in default — the store and the restore
        // each log their own why.
        if (StartupLayout is { } startup
            && await _layouts.LoadAsync(startup).ContinueOnSameContext() is { } saved
            && _layouts.Restore(saved))
            return saved;

        return _layouts.CreateDefault();
    }

    // Puts a fresh root live: the window manager targets it, the control shows it.
    private void SwapLayout(IRootDock layout)
    {
        _workspace.Root = layout;
        _control!.Layout = layout;
    }

    /// <summary>
    /// Surfaces the layout's floating windows: a restored layout's floats exist only in the dock model
    /// until <c>IRootDock.ShowWindows</c> runs (without it, panels saved floating never reappear and the
    /// main window looks empty). At bind time the control isn't in the (still-hidden) main window's
    /// visual tree yet, so the call defers to the control's Loaded — by then the window is on screen to
    /// own the floats.
    /// </summary>
    private void ShowFloatingWindows()
    {
        if (_control is not { } control)
            return;

        if (control.IsLoaded)
        {
            Show(control);
            return;
        }

        void OnLoaded(object? sender, RoutedEventArgs args)
        {
            control.Loaded -= OnLoaded;
            Show(control);
            Refresh();
        }

        control.Loaded += OnLoaded;

        void Show(DockControl target)
        {
            if (target.Layout is not IRootDock root)
                return;

            root.ShowWindows.Execute(null);

            // Belt over the WindowOpened hook: every shown float must end up drag-dockable.
            if (root.Windows is { } windows)
            {
                foreach (var window in windows)
                    _workspace.FocusFloatContent(window);
            }
        }
    }

    /// <summary>Closes the outgoing layout's floating windows before a swap (reset/load) abandons its
    /// dock model — otherwise their host windows linger over the replacement layout.</summary>
    private void CloseFloatingWindows()
    {
        if (_workspace.Root is { } root)
            root.ExitWindows.Execute(null);
    }
}
