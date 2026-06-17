using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Toybox.Studio.Widgets.Behaviors;

/// <summary>
/// Runs a command when a control is double-tapped — used to start inline rename from an entity's name label
/// without routing the event through view code-behind.
/// </summary>
public static class DoubleTapCommand
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>("Command", typeof(DoubleTapCommand));

    public static void SetCommand(Control control, ICommand? value) => control.SetValue(CommandProperty, value);
    public static ICommand? GetCommand(Control control) => control.GetValue(CommandProperty);

    static DoubleTapCommand()
    {
        CommandProperty.Changed.AddClassHandler<Control>(OnCommandChanged);
    }

    private static void OnCommandChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.DoubleTapped -= OnDoubleTapped;
        if (args.GetNewValue<ICommand?>() is not null)
            control.DoubleTapped += OnDoubleTapped;
    }

    private static void OnDoubleTapped(object? sender, TappedEventArgs args)
    {
        if (sender is not Control control || GetCommand(control) is not { } command || !command.CanExecute(null))
            return;

        command.Execute(null);
        // Consume the gesture so it doesn't also reach the host (e.g. toggling a tree node's expansion).
        args.Handled = true;
    }
}
