using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia;
using Toybox.Studio.Splitting;

namespace Toybox.Studio.Splitting;

/// <summary>
/// The draggable border between a <see cref="SplitBranch"/>'s two children. Dragging it maps the pointer
/// straight onto the branch's <see cref="SplitBranch.Ratio"/> and rewrites the owning grid's star
/// column/row sizes live — so the panes resize under the pointer with no tree rebuild (the viewports
/// keep streaming). It is a thin line that brightens on hover and shows the matching resize cursor.
/// </summary>
public sealed class SplitDivider : Border
{
    /// <summary>The divider's hit width/height — a touch wider than the drawn line so it's easy to grab.</summary>
    public const double Thickness = 6;

    private readonly SplitBranch _branch;
    private readonly Grid _grid;
    private readonly SplitOrientation _orientation;
    private bool _dragging;

    public SplitDivider(SplitBranch branch, Grid grid, SplitOrientation orientation)
    {
        _branch = branch;
        _grid = grid;
        _orientation = orientation;

        Background = Brushes.Transparent;
        Cursor = new Cursor(orientation == SplitOrientation.Horizontal
            ? StandardCursorType.SizeWestEast
            : StandardCursorType.SizeNorthSouth);

        if (orientation == SplitOrientation.Horizontal)
            Width = Thickness;
        else
            Height = Thickness;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (!_dragging)
            Background = HighlightBrush();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!_dragging)
            Background = Brushes.Transparent;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _dragging = true;
        Background = HighlightBrush();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
            return;

        var point = e.GetPosition(_grid);
        var extent = _orientation == SplitOrientation.Horizontal ? _grid.Bounds.Width : _grid.Bounds.Height;
        if (extent <= 0)
            return;

        var along = _orientation == SplitOrientation.Horizontal ? point.X : point.Y;
        Apply(Math.Clamp(along / extent, SplitBranch.MinRatio, 1 - SplitBranch.MinRatio));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
            return;

        _dragging = false;
        e.Pointer.Capture(null);
        if (!IsPointerOver)
            Background = Brushes.Transparent;
        e.Handled = true;
    }

    /// <summary>Sets the branch ratio and rewrites the two star sizes in place (used by a drag and by a
    /// live split gesture driving the freshly created divider).</summary>
    public void Apply(double ratio)
    {
        _branch.Ratio = ratio;
        var first = new GridLength(ratio, GridUnitType.Star);
        var second = new GridLength(1 - ratio, GridUnitType.Star);

        if (_orientation == SplitOrientation.Horizontal)
        {
            _grid.ColumnDefinitions[0].Width = first;
            _grid.ColumnDefinitions[2].Width = second;
        }
        else
        {
            _grid.RowDefinitions[0].Height = first;
            _grid.RowDefinitions[2].Height = second;
        }
    }

    private IBrush HighlightBrush() =>
        Application.Current?.TryGetResource("ThemeAccentSolidBrush", null, out var value) == true
        && value is IBrush brush
            ? brush
            : new SolidColorBrush(Color.FromRgb(0x98, 0x88, 0xEB));
}
