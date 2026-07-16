using Avalonia.Controls;
using Avalonia;

namespace Toybox.Studio.Behaviors;

/// <summary>
/// Collapses one <see cref="Grid"/> column to zero width when a bound flag goes false, and restores its
/// authored width when true — so a splitter pane (e.g. the Asset Viewer's property inspector) can be
/// hidden and give its space back to the remaining columns, without code-behind. Set
/// <see cref="ColumnProperty"/> to the column index and bind <see cref="VisibleProperty"/> to the flag;
/// hide the column's own controls (and any adjacent <c>GridSplitter</c>) with <c>IsVisible</c> the same
/// way. Binding a <c>ColumnDefinition.Width</c> directly isn't possible (a definition has no
/// DataContext), which is why this drives it from the grid.
/// </summary>
public static class CollapsibleColumnBehavior
{
    public static readonly AttachedProperty<int> ColumnProperty =
        AvaloniaProperty.RegisterAttached<Grid, int>("Column", typeof(CollapsibleColumnBehavior), -1);

    public static readonly AttachedProperty<bool> VisibleProperty =
        AvaloniaProperty.RegisterAttached<Grid, bool>("Visible", typeof(CollapsibleColumnBehavior), true);

    // The width to restore when the column is shown again — captured the first time it collapses, so a
    // splitter drag while it was visible is preserved across a hide/show cycle.
    private static readonly AttachedProperty<GridLength?> SavedWidthProperty =
        AvaloniaProperty.RegisterAttached<Grid, GridLength?>("SavedWidth", typeof(CollapsibleColumnBehavior));

    static CollapsibleColumnBehavior()
    {
        VisibleProperty.Changed.AddClassHandler<Grid>((grid, _) => Apply(grid));
        ColumnProperty.Changed.AddClassHandler<Grid>((grid, _) => Apply(grid));
    }

    public static void SetColumn(Grid grid, int value) => grid.SetValue(ColumnProperty, value);
    public static int GetColumn(Grid grid) => grid.GetValue(ColumnProperty);

    public static void SetVisible(Grid grid, bool value) => grid.SetValue(VisibleProperty, value);
    public static bool GetVisible(Grid grid) => grid.GetValue(VisibleProperty);

    private static void Apply(Grid grid)
    {
        var index = grid.GetValue(ColumnProperty);
        if (index < 0 || index >= grid.ColumnDefinitions.Count)
            return;

        var column = grid.ColumnDefinitions[index];
        if (grid.GetValue(VisibleProperty))
        {
            if (grid.GetValue(SavedWidthProperty) is { } saved)
                column.Width = saved;
        }
        else
        {
            grid.SetValue(SavedWidthProperty, column.Width);
            column.Width = new GridLength(0);
        }
    }
}
