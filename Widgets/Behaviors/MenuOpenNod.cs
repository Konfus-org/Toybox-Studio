using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Toybox.Studio.Services.Motion;

namespace Toybox.Studio.Widgets.Behaviors;

/// <summary>
/// When a <see cref="MenuItem"/>'s submenu opens, the opening dropdown gives a small "nod": a top-level menu
/// (File / Build / …) nods DOWN as it drops in, a sub-option's submenu nods RIGHT as it slides out to the
/// side. Mirrors <see cref="EnterNod"/> (a dip-and-return on a Visual's <c>TranslateTransform</c>) but on the
/// popup content, and scales with the live <c>AnimationIntensity</c> resource — nothing at intensity 0.
///
/// Enabled app-wide via a Style setter on MenuItem in MenuStyle.
/// </summary>
public static class MenuOpenNod
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<MenuItem, bool>("Enabled", typeof(MenuOpenNod));

    public static void SetEnabled(MenuItem item, bool value) => item.SetValue(EnabledProperty, value);
    public static bool GetEnabled(MenuItem item) => item.GetValue(EnabledProperty);

    static MenuOpenNod()
    {
        EnabledProperty.Changed.AddClassHandler<MenuItem>(OnEnabledChanged);
    }

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

        var intensity = Intensity(item);
        if (intensity <= 0)
            return;

        // The dropdown lives in the item's templated Popup; animate its content (the panel border).
        if (item.GetVisualDescendants().OfType<Popup>().FirstOrDefault()?.Child is not Visual content)
            return;

        // A top-level menu drops DOWN; a sub-option's submenu (it has a MenuItem ancestor) slides RIGHT.
        var horizontal = item.GetLogicalAncestors().OfType<MenuItem>().Any();
        Nod(content, horizontal, intensity);
    }

    // A quick dip-and-return along one axis (X for a sideways submenu, Y for a top-level dropdown), matching
    // the EnterNod feel. The keyframes return to 0, so the panel is left exactly where layout placed it.
    private static void Nod(Visual content, bool horizontal, double intensity)
    {
        var property = horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        var dip = (horizontal ? 4.0 : 3.0) * intensity;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(170),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(property, 0d) } },
                new KeyFrame { Cue = new Cue(0.4d), Setters = { new Setter(property, dip) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(property, 0d) } },
            },
        };
        _ = animation.RunAsync(content);
    }

    private static double Intensity(Visual visual) =>
        visual.TryFindResource(MotionTokens.IntensityKey, out var value) && value is double intensity ? intensity : 0;
}
