using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Toybox.Studio.NodeGraph;

/// <summary>
/// Reflects whether the pointer is over a node card onto its <see cref="EntityNodeViewModel.IsHovered"/>, so
/// the graph can fan the card's deck while any of its members is hovered. Attached to the card root.
/// </summary>
public static class NodeHoverBehavior
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(NodeHoverBehavior));

    static NodeHoverBehavior() => EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);
    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    private static void OnEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.PointerEntered -= OnPointerEntered;
        control.PointerExited -= OnPointerExited;
        if (args.GetNewValue<bool>())
        {
            control.PointerEntered += OnPointerEntered;
            control.PointerExited += OnPointerExited;
        }
    }

    private static void OnPointerEntered(object? sender, PointerEventArgs args)
    {
        if (sender is Control { DataContext: EntityNodeViewModel node })
            node.IsHovered = true;
    }

    private static void OnPointerExited(object? sender, PointerEventArgs args)
    {
        if (sender is Control { DataContext: EntityNodeViewModel node })
            node.IsHovered = false;
    }
}
