using Avalonia.Controls;
using Avalonia;

namespace Toybox.Studio.Searching;

/// <summary>
/// A small activity spinner. With <see cref="Progress"/> unset (<c>null</c>) it spins indeterminately;
/// with a 0..1 value it draws a determinate arc showing how far along the work is. Hosts toggle its
/// <c>IsVisible</c>; Avalonia halts the animation while hidden, so it is free when idle.
/// </summary>
public partial class Spinner : UserControl
{
    public static readonly StyledProperty<double?> ProgressProperty =
        AvaloniaProperty.Register<Spinner, double?>(nameof(Progress));

    public Spinner()
    {
        InitializeComponent();
    }

    /// <summary>Fractional progress (0..1), or <c>null</c> for the indeterminate spin.</summary>
    public double? Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }
}
