using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Splitting;

namespace Toybox.Studio.Viewport;

/// <summary>
/// Backs <see cref="ViewportSplitView"/>: a reusable Blender-style split grid of viewports (embedded by
/// a host dockable — the world editor's <c>WorldViewerView</c>, or any future viewport panel). It owns
/// the panel's <see cref="SplitLayout"/> (persisted with the dock layout, so the split survives a restart)
/// and mints one <see cref="ViewportViewModel"/> per pane through the pane factory the host supplies — each
/// pane a full viewport with its own stream (and whatever overlays the host wires). It is the split
/// container's <see cref="IPaneSource"/>, so it also disposes a pane's viewport when its pane is joined
/// away, and every remaining pane when the panel itself closes.
/// </summary>
public partial class ViewportSplitViewModel : ObservableObject, ISplitHost, IPaneSource, IDisposable
{
    private readonly Func<ViewportViewModel> _createPane;
    private readonly HashSet<ViewportViewModel> _openPanes = [];

    /// <summary>Builds the grid over a host-supplied pane factory: <paramref name="createPane"/> mints one
    /// fresh, independent viewport (its own stream and overlays) per split pane. The host owns what a pane
    /// is; this grid only owns the splitting and each pane's lifetime.</summary>
    public ViewportSplitViewModel(Func<ViewportViewModel> createPane)
    {
        _createPane = createPane;
    }

    /// <summary>The split tree the container renders. Bound by the view; set from the persisted layout
    /// on materialize (idempotent — re-binding the same instance is a no-op).</summary>
    [ObservableProperty]
    public partial SplitLayout Layout { get; private set; } = new();

    public void BindSplitLayout(SplitLayout layout) => Layout = layout;

    public object CreatePane(SplitLeaf leaf)
    {
        var viewport = _createPane();
        viewport.BindToolbars(leaf.Toolbars);
        _openPanes.Add(viewport);
        return viewport;
    }

    public void ReleasePane(object pane)
    {
        if (pane is ViewportViewModel viewport && _openPanes.Remove(viewport))
            viewport.Dispose();
    }

    public void Dispose()
    {
        foreach (var pane in _openPanes)
            pane.Dispose();
        _openPanes.Clear();
    }
}
