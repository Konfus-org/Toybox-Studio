using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Toybox.Studio.Behaviors;

/// <summary>
/// Runs a command the first time the pointer enters a control — used to lazily fetch a tile's hover-HUD stats
/// when it's first hovered, without routing the event through view code-behind. Cheap and idempotent on the
/// view-model side (the command is expected to no-op after its first run).
/// </summary>
public static class PointerEnterCommandBehavior
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>("Command", typeof(PointerEnterCommandBehavior));

    static PointerEnterCommandBehavior()
    {
        CommandProperty.Changed.AddClassHandler<Control>(OnCommandChanged);
    }

    public static void SetCommand(Control control, ICommand? value) => control.SetValue(CommandProperty, value);

    public static ICommand? GetCommand(Control control) => control.GetValue(CommandProperty);

    private static void OnCommandChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.PointerEntered -= OnPointerEntered;
        if (args.GetNewValue<ICommand?>() is not null)
            control.PointerEntered += OnPointerEntered;
    }

    private static void OnPointerEntered(object? sender, PointerEventArgs args)
    {
        if (sender is Control control && GetCommand(control) is { } command && command.CanExecute(null))
            command.Execute(null);
    }
}
