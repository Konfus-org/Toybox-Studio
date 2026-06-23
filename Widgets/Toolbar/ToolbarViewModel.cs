using System;
using System.Collections.ObjectModel;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.World;

namespace Toybox.Studio.Widgets.Toolbar;

/// <summary>
/// The viewport's movable, data-driven toolbar: a list of tools (built from the persisted
/// <see cref="ToolbarLayout"/>) that the user can re-order by dragging and dock to any viewport edge. All
/// mutations write back into the same <see cref="ToolbarLayout"/> instance the hosting dock tool owns, so the
/// exit-time layout save persists them — no per-edit save.
/// </summary>
public sealed partial class ToolbarViewModel : ObservableObject, IDisposable
{
    private readonly ToolbarLayout _layout;

    public ToolbarViewModel(
        ToolbarLayout layout, ToolCommandRunner runner, ToolbarState state, EngineWatcher watcher)
    {
        _layout = layout;
        DockedEdge = layout.DockedEdge;
        foreach (var item in layout.Tools)
            Tools.Add(new ToolbarItemViewModel(item, runner, state, watcher));
    }

    /// <summary>The persisted layout this toolbar reads and writes (the instance the dock tool serializes).</summary>
    public ToolbarLayout Layout => _layout;

    /// <summary>The tool buttons, in display order.</summary>
    public ObservableCollection<ToolbarItemViewModel> Tools { get; } = [];

    /// <summary>The viewport edge the toolbar is docked against.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Orientation))]
    [NotifyPropertyChangedFor(nameof(HorizontalAlignment))]
    [NotifyPropertyChangedFor(nameof(VerticalAlignment))]
    public partial ToolbarEdge DockedEdge { get; private set; }

    /// <summary>Horizontal along the top/bottom edges, vertical along the left/right edges.</summary>
    public Orientation Orientation =>
        DockedEdge is ToolbarEdge.Left or ToolbarEdge.Right ? Orientation.Vertical : Orientation.Horizontal;

    /// <summary>Overlay alignment derived from the docked edge (the toolbar hugs that edge, centred along it).</summary>
    public HorizontalAlignment HorizontalAlignment => DockedEdge switch
    {
        ToolbarEdge.Left => HorizontalAlignment.Left,
        ToolbarEdge.Right => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Center,
    };

    public VerticalAlignment VerticalAlignment => DockedEdge switch
    {
        ToolbarEdge.Bottom => VerticalAlignment.Bottom,
        ToolbarEdge.Top => VerticalAlignment.Top,
        _ => VerticalAlignment.Center,
    };

    /// <summary>Docks the toolbar to <paramref name="edge"/> and persists it into the layout.</summary>
    public void SetDockedEdge(ToolbarEdge edge)
    {
        DockedEdge = edge;
        _layout.DockedEdge = edge;
    }

    /// <summary>Moves a tool from one index to another, mutating the persisted order in lockstep.</summary>
    public void MoveTool(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= Tools.Count || to >= Tools.Count)
            return;

        var item = _layout.Tools[from];
        _layout.Tools.RemoveAt(from);
        _layout.Tools.Insert(to, item);
        Tools.Move(from, to);
    }

    public void Dispose()
    {
        foreach (var tool in Tools)
            tool.Dispose();
    }
}
