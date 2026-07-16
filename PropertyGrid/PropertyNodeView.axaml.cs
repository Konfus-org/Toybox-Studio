using Avalonia;
using Avalonia.Controls;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// One property row. Renders a top-level composite/list node as a full-width accent section band, a nested one
/// as an ordinary indented row with a disclosure twisty, and a leaf as a label/value row — the shared label
/// column, groove separators, depth shading and indent giving the grid its table look. <see cref="Depth"/> is
/// threaded down the recursive tree by the view (a child binds its depth to its parent's + 1), so the node
/// model carries no depth.
/// </summary>
public partial class PropertyNodeView : UserControl
{
    public static readonly StyledProperty<int> DepthProperty =
        AvaloniaProperty.Register<PropertyNodeView, int>(nameof(Depth));

    public PropertyNodeView()
    {
        InitializeComponent();
    }

    /// <summary>The row's nesting depth (0 at the top level), driving its indent and warm depth tint. The root
    /// rows a card renders leave it at the default 0; each child row binds to its parent's depth + 1.</summary>
    public int Depth
    {
        get => GetValue(DepthProperty);
        set => SetValue(DepthProperty, value);
    }
}
