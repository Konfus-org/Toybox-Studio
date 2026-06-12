using Avalonia;
using Avalonia.Controls;

namespace Toybox.Studio.Widgets.Ghost;

/// <summary>
/// Reusable empty/loading placeholder: a ghost with a message (shown below it and as a tooltip).
/// Bind <see cref="Message"/> and toggle the control's IsVisible from the host.
/// </summary>
public partial class GhostView : UserControl
{
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<GhostView, string?>(nameof(Message));

    public GhostView()
    {
        InitializeComponent();
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
}
