using Avalonia;
using Avalonia.Controls;

namespace Toybox.Studio.Widgets.Ghost;

/// <summary>
/// Reusable loading placeholder: a ghost that wiggles back and forth above an indeterminate progress
/// bar and a message (shown below it and as a tooltip). Bind <see cref="Message"/> and toggle the
/// control's IsVisible from the host.
/// </summary>
public partial class LoadingGhostView : UserControl
{
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<LoadingGhostView, string?>(nameof(Message), defaultValue: "Loading…");

    public LoadingGhostView()
    {
        InitializeComponent();
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
}
