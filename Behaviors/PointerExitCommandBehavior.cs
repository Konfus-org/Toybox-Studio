using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using System.Windows.Input;

namespace Toybox.Studio.Behaviors;

/// <summary>
/// Runs a command when the pointer leaves a control — the companion to <see cref="PointerEnterCommandBehavior"/>, used
/// to dismiss the browser's hover-preview card once the pointer leaves the asset grid.
/// </summary>
public static class PointerExitCommandBehavior
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>("Command", typeof(PointerExitCommandBehavior));

    static PointerExitCommandBehavior()
    {
        CommandProperty.Changed.AddClassHandler<Control>(OnCommandChanged);
    }

    public static void SetCommand(Control control, ICommand? value) => control.SetValue(CommandProperty, value);

    public static ICommand? GetCommand(Control control) => control.GetValue(CommandProperty);

    private static void OnCommandChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.PointerExited -= OnPointerExited;
        if (args.GetNewValue<ICommand?>() is not null)
            control.PointerExited += OnPointerExited;
    }

    private static void OnPointerExited(object? sender, PointerEventArgs args)
    {
        if (sender is Control control && GetCommand(control) is { } command && command.CanExecute(null))
            command.Execute(null);
    }
}
