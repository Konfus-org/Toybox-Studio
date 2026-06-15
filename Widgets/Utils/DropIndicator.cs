using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Toybox.Studio.Widgets.Utils;

/// <summary>Where a drag would land relative to the row under the pointer.</summary>
public enum DropMarker
{
    /// <summary>Insert above the row (a line at its top edge).</summary>
    Before,

    /// <summary>Drop onto the row itself — reparent/contain (the row is highlighted).</summary>
    Onto,

    /// <summary>Insert below the row (a line at its bottom edge).</summary>
    After,
}

/// <summary>
/// A single, shared drag-and-drop drop indicator drawn on the adorner layer: a highlight box when dropping
/// <see cref="DropMarker.Onto"/> a target, or a horizontal insertion line at the target's top/bottom edge
/// when reordering before/after it. Reused across the world tree and the property-grid lists — only one
/// indicator is ever visible, so callers just <see cref="Show"/> the current target or <see cref="Clear"/>.
/// </summary>
public static class DropIndicator
{
    // Accent-ish colours; the line is solid, the onto-highlight a translucent fill with a solid edge.
    private static readonly IBrush LineBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x8B, 0xFF));
    private static readonly IBrush FillBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x3D, 0x8B, 0xFF));

    private static Border? _onto;
    private static Panel? _linePanel;
    private static Border? _lineBar;

    /// <summary>Shows the indicator on <paramref name="target"/> for the given drop position.</summary>
    public static void Show(Control target, DropMarker marker)
    {
        var layer = AdornerLayer.GetAdornerLayer(target);
        if (layer is null)
        {
            Clear();
            return;
        }

        EnsureVisuals();
        var shown = marker == DropMarker.Onto ? (Control)_onto! : _linePanel!;
        var hidden = marker == DropMarker.Onto ? (Control)_linePanel! : _onto!;

        Detach(hidden);
        if (!ReferenceEquals(shown.Parent, layer))
        {
            Detach(shown);
            layer.Children.Add(shown);
        }

        AdornerLayer.SetAdornedElement(shown, target);
        if (marker != DropMarker.Onto)
            _lineBar!.VerticalAlignment =
                marker == DropMarker.Before ? VerticalAlignment.Top : VerticalAlignment.Bottom;
    }

    /// <summary>Hides the indicator (drag ended or left every target).</summary>
    public static void Clear()
    {
        Detach(_onto);
        Detach(_linePanel);
    }

    private static void Detach(Control? control)
    {
        if (control?.Parent is AdornerLayer layer)
            layer.Children.Remove(control);
    }

    private static void EnsureVisuals()
    {
        _onto ??= new Border
        {
            BorderThickness = new Thickness(2),
            BorderBrush = LineBrush,
            Background = FillBrush,
            CornerRadius = new CornerRadius(3),
            IsHitTestVisible = false,
        };

        if (_linePanel is null)
        {
            _lineBar = new Border
            {
                Height = 2,
                Background = LineBrush,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
            };
            _linePanel = new Panel { IsHitTestVisible = false, Children = { _lineBar } };
        }
    }
}
