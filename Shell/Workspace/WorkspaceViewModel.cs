using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Shell.Panels;
using Toybox.Studio.Utils;

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

    private Window? Owner => _control is null ? null : TopLevel.GetTopLevel(_control) as Window;

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

    /// <summary>Opens the dockable only if it isn't already open — used by the Play button for the viewport.</summary>
    public void EnsureOpen(string id)
    {
        if (Resolve(id) is not { } descriptor || _control?.Layout is not IRootDock root || Owner is not { } owner)
            return;

        _windows.EnsureOpen(descriptor, root, owner);
        Refresh();
    }

    /// <summary>Prompts for a name and saves the current arrangement as a named layout the user can restore.</summary>
    [RelayCommand]
    private async Task SaveLayout()
    {
        if (_control?.Layout is not IRootDock root)
            return;

        var name = await Popups.PromptForTextAsync("Save Layout", "Layout name", confirmText: "Save")
            .ContinueOnAnyContext();
        if (string.IsNullOrWhiteSpace(name))
            return;

        _store.Save(name, root);
        _log.Info($"Saved layout '{name}'.");
    }

    /// <summary>Lets the user pick a saved layout and swaps the live arrangement to it.</summary>
    [RelayCommand]
    private async Task LoadLayout()
    {
        var names = _store.List();
        if (names.Count == 0)
        {
            await Popups.ShowMessageAsync("Load Layout", "No saved layouts yet.").ContinueOnAnyContext();
            return;
        }

        var options = names.Select(name => new CatalogItem(name, name, "Saved layout")).ToList();
        if (await CatalogPicker.ShowAsync("Load Layout", "No saved layouts.", options).ContinueOnAnyContext()
            is not { } pick)
            return;

        if (_store.Load(pick.Key) is not { } layout)
        {
            await Popups.ShowErrorAsync("Load Layout", $"Couldn't load layout '{pick.Key}'.")
                .ContinueOnAnyContext();
            return;
        }

        if (_control is null)
            return;

        // Same rehydrate-then-swap sequence BuildInitialLayout/ResetLayout use to attach a fresh root.
        _windows.InitLayout(layout);
        _windows.AttachContent(layout);
        _control.Layout = layout;
        Refresh();
    }

    /// <summary>Persists the current layout as the working layout. Called on app exit.</summary>
    public void SaveLastLayout()
    {
        if (_control?.Layout is IRootDock root)
            _store.SaveLast(root);
    }

    /// <summary>The view-model of the currently focused dockable (or null) — what File ▸ Save targets.</summary>
    public object? FocusedDockable() => _windows.FocusedViewModel();

    /// <summary>Every open data panel (distinct) — the source for File ▸ Save All and the app-close prompt.</summary>
    public IEnumerable<DataPanel> OpenPanels() => _windows.OpenPanels();

    private DockableDescriptor? Resolve(string id) => _byId.GetValueOrDefault(id);

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
