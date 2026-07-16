using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia;
using System.Windows.Input;

namespace Toybox.Studio.WorldTree;

/// <summary>
/// Wires an inline-rename <see cref="TextBox"/> to its row's edit state without view code-behind: when
/// <c>Active</c> flips true (the row entered rename) it focuses the box and selects its text, and it runs
/// <c>CommitCommand</c> when the box loses focus, so clicking away confirms the rename exactly as Enter
/// does (a no-op after Escape, which has already left edit mode). Enter/Escape themselves are the box's own
/// XAML key bindings.
/// </summary>
public static class RenameBoxBehavior
{
    public static readonly AttachedProperty<bool> ActiveProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("Active", typeof(RenameBoxBehavior));

    public static readonly AttachedProperty<ICommand?> CommitCommandProperty =
        AvaloniaProperty.RegisterAttached<TextBox, ICommand?>("CommitCommand", typeof(RenameBoxBehavior));

    static RenameBoxBehavior()
    {
        ActiveProperty.Changed.AddClassHandler<TextBox>(OnActiveChanged);
        CommitCommandProperty.Changed.AddClassHandler<TextBox>(OnCommitCommandChanged);
    }

    public static void SetActive(TextBox box, bool value) => box.SetValue(ActiveProperty, value);
    public static bool GetActive(TextBox box) => box.GetValue(ActiveProperty);
    public static void SetCommitCommand(TextBox box, ICommand? value) => box.SetValue(CommitCommandProperty, value);
    public static ICommand? GetCommitCommand(TextBox box) => box.GetValue(CommitCommandProperty);

    private static void OnActiveChanged(TextBox box, AvaloniaPropertyChangedEventArgs args)
    {
        if (!args.GetNewValue<bool>())
            return;

        // The box only just became visible; defer focus to the next input pass so it's laid out.
        Dispatcher.UIThread.Post(
            () =>
            {
                box.Focus();
                box.SelectAll();
            },
            DispatcherPriority.Input);
    }

    private static void OnCommitCommandChanged(TextBox box, AvaloniaPropertyChangedEventArgs args)
    {
        box.LostFocus -= OnLostFocus;
        if (args.GetNewValue<ICommand?>() is not null)
            box.LostFocus += OnLostFocus;
    }

    private static void OnLostFocus(object? sender, RoutedEventArgs args)
    {
        if (sender is TextBox box && GetCommitCommand(box) is { } command && command.CanExecute(null))
            command.Execute(null);
    }
}
