using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Shared chrome for one property row: a fixed label gutter (with an expander chevron / nesting elbow and
/// optional type icon), a state column (read-only lock or "set" dot), and a value well that hosts the
/// per-type editor as its <see cref="ContentControl.Content"/>. Every leaf widget view wraps its editor in
/// a PropertyRow so the table layout, dividers, shading, and indicators are defined in exactly one place.
/// </summary>
public sealed class PropertyRow : ContentControl
{
    public static readonly StyledProperty<string?> HeaderTextProperty =
        AvaloniaProperty.Register<PropertyRow, string?>(nameof(HeaderText));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<PropertyRow, string?>(nameof(Description));

    public static readonly StyledProperty<int> DepthProperty =
        AvaloniaProperty.Register<PropertyRow, int>(nameof(Depth));

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<PropertyRow, bool>(nameof(IsReadOnly));

    public static readonly StyledProperty<bool> IsParentProperty =
        AvaloniaProperty.Register<PropertyRow, bool>(nameof(IsParent));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<PropertyRow, bool>(nameof(IsExpanded), defaultValue: true);

    public static readonly StyledProperty<string?> IconNameProperty =
        AvaloniaProperty.Register<PropertyRow, string?>(nameof(IconName));

    public static readonly StyledProperty<string?> IconColorProperty =
        AvaloniaProperty.Register<PropertyRow, string?>(nameof(IconColor));

    public PropertyRow()
    {
        // A row hides itself when its backing view-model is filtered out by the grid search. Bound here,
        // in one place, rather than on every widget view; the "Visible" path resolves against the row's
        // DataContext (the PropertyViewModel), which a ControlTheme setter binding does not reliably do.
        this.Bind(IsVisibleProperty, new Binding(nameof(PropertyViewModel.Visible)) { FallbackValue = true });
    }

    public string? HeaderText
    {
        get => GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public int Depth
    {
        get => GetValue(DepthProperty);
        set => SetValue(DepthProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>True for object/array rows that own a collapsible sub-tree: shows the collapse chevron.</summary>
    public bool IsParent
    {
        get => GetValue(IsParentProperty);
        set => SetValue(IsParentProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public string? IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public string? IconColor
    {
        get => GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }
}
