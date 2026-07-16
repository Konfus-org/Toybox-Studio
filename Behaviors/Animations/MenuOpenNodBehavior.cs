using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// When a <see cref="MenuItem"/>'s submenu opens, the opening dropdown gives a small nod (a
/// <see cref="NodClip"/> dip-and-return) in the direction it just moved: a top-level menu (File / Build / …)
/// nods DOWN as it drops in, a sub-option's submenu nods RIGHT as it slides out to the side. Enabled
/// app-wide via a Style setter on MenuItem in MenuStyle.
/// </summary>
public static class MenuOpenNodBehavior
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<MenuItem, bool>("Enabled", typeof(MenuOpenNodBehavior));

    private static readonly NodClip DropNod = new();
    private static readonly NodClip SideNod = new() { Orientation = Orientation.Horizontal, Depth = 4 };

    static MenuOpenNodBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<MenuItem>(OnEnabledChanged);
    }

    public static void SetEnabled(MenuItem item, bool value) => item.SetValue(EnabledProperty, value);
    public static bool GetEnabled(MenuItem item) => item.GetValue(EnabledProperty);

    private static void OnEnabledChanged(MenuItem item, AvaloniaPropertyChangedEventArgs args)
    {
        // Detach first so the handler is wired exactly once regardless of how the flag toggles.
        item.RemoveHandler(MenuItem.SubmenuOpenedEvent, OnSubmenuOpened);
        if (args.GetNewValue<bool>())
            item.AddHandler(MenuItem.SubmenuOpenedEvent, OnSubmenuOpened);
    }

    private static void OnSubmenuOpened(object? sender, RoutedEventArgs args)
    {
        // SubmenuOpened bubbles up through ancestor menu items — only nod for the item that actually opened.
        if (sender is not MenuItem item || !ReferenceEquals(args.Source, item))
            return;

        // The dropdown lives in the item's templated Popup; animate its content (the panel border).
        if (item.GetVisualDescendants().OfType<Popup>().FirstOrDefault()?.Child is not Visual content)
            return;

        // A top-level menu drops DOWN; a sub-option's submenu (it has a MenuItem ancestor) slides RIGHT.
        var horizontal = item.GetLogicalAncestors().OfType<MenuItem>().Any();
        _ = MotionPlayer.PlayAsync(content, horizontal ? SideNod : DropNod);
    }
}
