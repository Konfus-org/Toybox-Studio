using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Toybox.Studio.Widgets.Behaviors;

/// <summary>
/// Inline-rename glue for a <see cref="TextBox"/>: when <see cref="ActiveProperty"/> turns true the box takes
/// focus and selects its text; Enter or focus-loss runs <see cref="CommitCommandProperty"/> and Escape runs
/// <see cref="CancelCommandProperty"/>. Keeps the rename interaction in a behavior rather than view
/// code-behind, matching the project's no-routing-in-views convention.
/// </summary>
public static class RenameBox
{
    public static readonly AttachedProperty<bool> ActiveProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("Active", typeof(RenameBox));

    public static readonly AttachedProperty<ICommand?> CommitCommandProperty =
        AvaloniaProperty.RegisterAttached<TextBox, ICommand?>("CommitCommand", typeof(RenameBox));

    public static readonly AttachedProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.RegisterAttached<TextBox, ICommand?>("CancelCommand", typeof(RenameBox));

    static RenameBox()
    {
        ActiveProperty.Changed.AddClassHandler<TextBox>(OnActiveChanged);
    }

    public static void SetActive(TextBox box, bool value) => box.SetValue(ActiveProperty, value);
    public static bool GetActive(TextBox box) => box.GetValue(ActiveProperty);
    public static void SetCommitCommand(TextBox box, ICommand? value) => box.SetValue(CommitCommandProperty, value);
    public static ICommand? GetCommitCommand(TextBox box) => box.GetValue(CommitCommandProperty);
    public static void SetCancelCommand(TextBox box, ICommand? value) => box.SetValue(CancelCommandProperty, value);
    public static ICommand? GetCancelCommand(TextBox box) => box.GetValue(CancelCommandProperty);

    private static void OnActiveChanged(TextBox box, AvaloniaPropertyChangedEventArgs args)
    {
        // Detach first so the handlers are wired exactly once regardless of how the flag toggles.
        box.KeyDown -= OnKeyDown;
        box.LostFocus -= OnLostFocus;
        if (!args.GetNewValue<bool>())
            return;

        box.KeyDown += OnKeyDown;
        box.LostFocus += OnLostFocus;

        // Focus + select once the box has actually been realized (it flips visible in this same change), so
        // a freshly added entity lands ready to type over its auto-generated name.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!GetActive(box))
                    return;
                box.Focus();
                box.SelectAll();
            },
            DispatcherPriority.Background);
    }

    private static void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs args)
    {
        if (sender is not TextBox box)
            return;

        if (args.Key == Avalonia.Input.Key.Enter)
        {
            Run(GetCommitCommand(box));
            args.Handled = true;
        }
        else if (args.Key == Avalonia.Input.Key.Escape)
        {
            Run(GetCancelCommand(box));
            args.Handled = true;
        }
    }

    private static void OnLostFocus(object? sender, RoutedEventArgs args)
    {
        if (sender is TextBox box)
            Run(GetCommitCommand(box));
    }

    private static void Run(ICommand? command)
    {
        if (command is not null && command.CanExecute(null))
            command.Execute(null);
    }
}
