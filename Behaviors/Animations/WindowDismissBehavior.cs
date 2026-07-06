using Avalonia;
using Avalonia.Controls;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// Fades a window out and closes it: bind <see cref="DismissProperty"/> to a view-model flag and, when it
/// flips true, the window fades away while its content slides gently down — the splash "dropping into" the
/// studio window on startup — then closes. Closing is functional, so it happens even at animation intensity
/// 0 (then immediately, with no motion). One-way: a dismissed window closes; the flag never un-dismisses.
/// The window needs a transparency level that supports alpha (e.g. <c>TransparencyLevelHint="Transparent"</c>)
/// for the fade to blend into what's behind it rather than into the surface's clear color.
/// </summary>
public static class WindowDismissBehavior
{
    public static readonly AttachedProperty<bool> DismissProperty =
        AvaloniaProperty.RegisterAttached<Window, bool>("Dismiss", typeof(WindowDismissBehavior));

    // Slower than the clip defaults: a whole window leaving needs more time to read than a control
    // gesture, especially over the first frames of the window taking over behind it.
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(420);
    private static readonly FadeClip Fade = new() { Duration = Duration };
    private static readonly SlideFadeClip Drop = new() { Duration = Duration, Slide = 22 };

    static WindowDismissBehavior()
    {
        DismissProperty.Changed.AddClassHandler<Window>(OnDismissChanged);
    }

    public static void SetDismiss(Window window, bool value) => window.SetValue(DismissProperty, value);
    public static bool GetDismiss(Window window) => window.GetValue(DismissProperty);

    private static void OnDismissChanged(Window window, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
            _ = DismissAsync(window);
    }

    private static async Task DismissAsync(Window window)
    {
        try
        {
            // The whole window fades (fading just the content would leave its background as a solid
            // rectangle) while the content slides down out from under it.
            var fades = new List<Task> { MotionPlayer.PlayAsync(window, Fade) };
            if (window.Content is Visual content)
                fades.Add(MotionPlayer.PlayAsync(content, Drop));

            await Task.WhenAll(fades);
        }
        finally
        {
            // Closing is functional: whatever happened to the fade, a dismissed window closes.
            window.Close();
        }
    }
}
