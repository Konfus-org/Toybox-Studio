using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Shell.Panels;

namespace Toybox.Studio.Shell.Workspace;

/// <summary>
/// The shell's window manager: owns the dock factory and the live layout, exposes the registered dockables
/// and their current state (<see cref="All"/> / <see cref="Docked"/> / <see cref="Floating"/> /
/// <see cref="Closed"/>), and drives open/focus, reset, and save. Keeping this here keeps
/// <c>ShellViewModel</c> out of the docking weeds.
/// </summary>
public sealed partial class WorkspaceViewModel : ObservableObject
{
    private readonly WindowManager _windows;
    private readonly LayoutStore _store;
    private readonly Logger _log;
    private readonly Dictionary<string, DockableDescriptor> _byId;

    private DockControl? _control;
    private IRootDock? _initialLayout;

    public WorkspaceViewModel(DockableCatalog catalog, LayoutStore store, Logger log)
    {
        _store = store;
        _log = log;
        _windows = new WindowManager(catalog);
        All = catalog.Dockables;
        _byId = All.ToDictionary(descriptor => descriptor.Id);
    }

    /// <summary>Every registered dockable. Drives the Windows menu.</summary>
    public IReadOnlyList<DockableDescriptor> All { get; }

    /// <summary>Dockables currently in the main docked layout.</summary>
    public ObservableCollection<DockableDescriptor> Docked { get; } = [];

    /// <summary>Dockables currently in their own floating window.</summary>
    public ObservableCollection<DockableDescriptor> Floating { get; } = [];

    /// <summary>Registered dockables that are not currently open anywhere.</summary>
    public ObservableCollection<DockableDescriptor> Closed { get; } = [];

    /// <summary>The <see cref="WindowManager"/> the <see cref="DockControl"/> uses as its <c>IFactory</c>.</summary>
    public WindowManager Factory => _windows;

    /// <summary>The layout to show on launch: the saved working layout, or the built-in default.</summary>
    public IRootDock InitialLayout => _initialLayout ??= BuildInitialLayout();

    /// <summary>Hooks up the live <see cref="DockControl"/>: assigns factory + layout, then tracks state.</summary>
    public void Bind(DockControl control)
    {
        _control = control;
        control.Factory = _windows;
        control.Layout = InitialLayout;
        Refresh();
    }

    /// <summary>Recomputes <see cref="Docked"/> / <see cref="Floating"/> / <see cref="Closed"/> from the live layout.</summary>
    public void Refresh()
    {
        Docked.Clear();
        Floating.Clear();
        Closed.Clear();

        var root = _control?.Layout as IRootDock;
        foreach (var descriptor in All)
        {
            if (root is not null && _windows.IsDocked(descriptor.Id, root))
                Docked.Add(descriptor);
            else if (root is not null && _windows.IsFloating(descriptor.Id, root))
                Floating.Add(descriptor);
            else
                Closed.Add(descriptor);
        }
    }

    /// <summary>Opens (or focuses, if already open) the dockable, opening it as a floating window if closed.</summary>
    [RelayCommand]
    public void OpenDockable(string id)
    {
        if (Resolve(id) is not { } descriptor || _control?.Layout is not IRootDock root || Owner is not { } owner)
            return;

        _windows.OpenOrFocus(descriptor, root, owner);
        Refresh();
    }

    /// <summary>Opens the dockable only if it isn't already open — used by the Play button for the viewport.</summary>
    public void EnsureOpen(string id)
    {
        if (Resolve(id) is not { } descriptor || _control?.Layout is not IRootDock root || Owner is not { } owner)
            return;

        _windows.EnsureOpen(descriptor, root, owner);
        Refresh();
    }

    /// <summary>Discards the current arrangement and rebuilds the built-in default layout.</summary>
    [RelayCommand]
    public void ResetLayout()
    {
        if (_control is null)
            return;

        var root = _windows.CreateDefaultLayout();
        _windows.InitLayout(root);
        _windows.AttachContent(root);
        _control.Layout = root;
        Refresh();
    }

    /// <summary>Persists the current layout as the working layout. Called on app exit.</summary>
    public void SaveCurrentLayout()
    {
        if (_control?.Layout is IRootDock root)
            _store.SaveLast(root);
    }

    /// <summary>The view-model of the currently focused dockable (or null) — what File ▸ Save targets.</summary>
    public object? FocusedDockable() => _windows.FocusedViewModel();

    /// <summary>Every open data panel (distinct) — the source for File ▸ Save All and the app-close prompt.</summary>
    public IEnumerable<DataPanel> OpenPanels() => _windows.OpenPanels();

    private DockableDescriptor? Resolve(string id) => _byId.GetValueOrDefault(id);

    private Window? Owner => _control is null ? null : TopLevel.GetTopLevel(_control) as Window;

    private IRootDock BuildInitialLayout()
    {
        if (_store.LoadLast() is { } saved)
        {
            try
            {
                _windows.InitLayout(saved);
                _windows.AttachContent(saved);
                return saved;
            }
            catch (Exception exception)
            {
                // A saved layout that can't be rehydrated (schema drift, partial file) falls back to default.
                _log.Warning($"Saved dock layout could not be restored; using the default. {exception.Message}");
            }
        }

        var root = _windows.CreateDefaultLayout();
        _windows.InitLayout(root);
        _windows.AttachContent(root);
        return root;
    }
}
