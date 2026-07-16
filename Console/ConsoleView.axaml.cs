using Avalonia.Controls;
using Avalonia;
using System.Windows.Input;

namespace Toybox.Studio.Console;

/// <summary>
/// Generic console view: a single selectable, tailing text stream (see <see cref="ConsoleTextView"/>) with an
/// optional search + clear toolbar. This view only exposes the toolbar toggle and the link command passed
/// through to the text stream — all the tailing/selection/link behaviour lives in the text control.
/// </summary>
public partial class ConsoleView : UserControl
{
    /// <summary>
    /// Whether the search + clear toolbar is shown; hide it for a bare line list (e.g. splash).
    /// </summary>
    public static readonly StyledProperty<bool> ShowToolbarProperty =
        AvaloniaProperty.Register<ConsoleView, bool>(nameof(ShowToolbar), defaultValue: true);

    /// <summary>
    /// Invoked with the clicked <see cref="ConsoleLink"/> when a link in the text is clicked; the host wires this
    /// to whatever a target means (the log console opens its <see cref="ConsoleLink.Target"/> as a file path, at
    /// its <see cref="ConsoleLink.Line"/>). Forwarded to the text stream.
    /// </summary>
    public static readonly StyledProperty<ICommand?> LinkCommandProperty =
        AvaloniaProperty.Register<ConsoleView, ICommand?>(nameof(LinkCommand));

    public ConsoleView()
    {
        InitializeComponent();
    }

    public bool ShowToolbar
    {
        get => GetValue(ShowToolbarProperty);
        set => SetValue(ShowToolbarProperty, value);
    }

    public ICommand? LinkCommand
    {
        get => GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }
}
