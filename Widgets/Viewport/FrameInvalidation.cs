using Avalonia;
using Avalonia.Controls;

namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// Attached behavior that calls <see cref="Visual.InvalidateVisual"/> on its target whenever the bound
/// <see cref="TickProperty"/> changes. Lets a view repaint in place — e.g. the viewport image, whose
/// <c>WriteableBitmap</c> is mutated frame to frame without ever changing reference — purely from XAML,
/// with no code-behind event wiring.
/// </summary>
public sealed class FrameInvalidation
{
    public static readonly AttachedProperty<int> TickProperty =
        AvaloniaProperty.RegisterAttached<FrameInvalidation, Control, int>("Tick");

    static FrameInvalidation() =>
        TickProperty.Changed.AddClassHandler<Control>((control, _) => control.InvalidateVisual());

    private FrameInvalidation()
    {
    }

    public static void SetTick(Control control, int value) => control.SetValue(TickProperty, value);

    public static int GetTick(Control control) => control.GetValue(TickProperty);
}
