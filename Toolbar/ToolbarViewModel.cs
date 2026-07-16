using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Events;
using Toybox.Studio.Toolbar;

namespace Toybox.Studio.Toolbar;

/// <summary>
/// A viewport's movable overlay toolbar: a row of action-driven <see cref="ToolbarItemViewModel"/>s,
/// dockable against any viewport edge or corner by dragging its grip. Placement changes write
/// through into the bound <see cref="ToolbarDockState"/> — the instance the panel's dock record
/// serializes under this toolbar's <see cref="Key"/> — so the exit-time layout save persists them
/// per viewport, no per-edit save. Concrete toolbars (the transform tools, the render layers) derive
/// to supply their items and re-derive their checked states from their domain's changed event.
/// </summary>
public partial class ToolbarViewModel : ObservableEventSubscriber
{
    // Never null so an unbound toolbar (a pre-toolbar saved layout) still docks and drags; binding
    // the persisted instance swaps it in.
    private ToolbarDockState _state = new();

    protected ToolbarViewModel(
        string key,
        ToolbarEdge defaultEdge,
        IReadOnlyList<ToolbarItemViewModel> tools,
        EventDispatcher events)
        : base(events)
    {
        Key = key;
        DefaultEdge = defaultEdge;
        Tools = tools;
        DockedEdge = defaultEdge;
    }

    /// <summary>The stable identity this toolbar's placement persists under in the panel's dock
    /// record (never shown; renaming it orphans saved placements).</summary>
    public string Key { get; }

    /// <summary>Where the toolbar docks before any user drag (and when a saved layout predates it).</summary>
    public ToolbarEdge DefaultEdge { get; }

    /// <summary>The tool buttons, in display order.</summary>
    public IReadOnlyList<ToolbarItemViewModel> Tools { get; }

    /// <summary>An optional editable numeric chip shown after the tools (the transform toolbar's snap
    /// amount): drag-to-scrub or type, the same control the property grid's number fields use. Null on
    /// toolbars that surface no value. Concrete toolbars build one and refresh it from their domain's
    /// changed event.</summary>
    public ToolbarNumberField? NumberField { get; protected set; }

    /// <summary>The viewport edge or corner the toolbar is docked against.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Orientation))]
    [NotifyPropertyChangedFor(nameof(HorizontalAlignment))]
    [NotifyPropertyChangedFor(nameof(VerticalAlignment))]
    public partial ToolbarEdge DockedEdge { get; private set; }

    /// <summary>Vertical along the left/right edges, horizontal along the top/bottom edges and in
    /// the corners (a corner toolbar reads as a row tucked under the panel's frame).</summary>
    public Orientation Orientation =>
        DockedEdge is ToolbarEdge.Left or ToolbarEdge.Right ? Orientation.Vertical : Orientation.Horizontal;

    /// <summary>Overlay alignment derived from the docked placement (the toolbar hugs its edge,
    /// centred along it; a corner hugs both of its edges).</summary>
    public HorizontalAlignment HorizontalAlignment => DockedEdge switch
    {
        ToolbarEdge.Left or ToolbarEdge.TopLeft or ToolbarEdge.BottomLeft => HorizontalAlignment.Left,
        ToolbarEdge.Right or ToolbarEdge.TopRight or ToolbarEdge.BottomRight => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Center,
    };

    public VerticalAlignment VerticalAlignment => DockedEdge switch
    {
        ToolbarEdge.Top or ToolbarEdge.TopLeft or ToolbarEdge.TopRight => VerticalAlignment.Top,
        ToolbarEdge.Bottom or ToolbarEdge.BottomLeft or ToolbarEdge.BottomRight => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Center,
    };

    /// <summary>Binds the persisted placement this toolbar reads and writes (the instance the panel's
    /// dock record serializes). Idempotent, as the workspace's repeated attach passes require.</summary>
    public void BindDockState(ToolbarDockState state)
    {
        if (ReferenceEquals(_state, state))
            return;

        _state = state;
        DockedEdge = state.Edge;
    }

    /// <summary>Docks the toolbar to <paramref name="edge"/> and persists it into the bound state.</summary>
    public void SetDockedEdge(ToolbarEdge edge)
    {
        DockedEdge = edge;
        _state.Edge = edge;
    }

    /// <summary>Re-derives every tool's checked state; concrete toolbars call this from their
    /// domain's changed-event handler.</summary>
    protected void RefreshTools()
    {
        foreach (var tool in Tools)
            tool.Refresh();
    }

    public override void Dispose()
    {
        base.Dispose();
        foreach (var tool in Tools)
            tool.Dispose();
    }
}
