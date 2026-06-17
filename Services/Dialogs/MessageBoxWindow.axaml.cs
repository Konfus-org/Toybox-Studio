using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Toybox.Studio.Services.Dialogs;

/// <summary>
/// A generic one-button message box.
/// </summary>
public partial class MessageBoxWindow : Window
{
    public MessageBoxWindow()
    {
        InitializeComponent();
    }

    public static Task ShowAsync(Window? owner, string title, string message)
    {
        var window = new MessageBoxWindow { Title = title };
        window.MessageText.Text = message;
        if (owner is not null)
            return window.ShowDialog(owner);

        window.Show();
        return Task.CompletedTask;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
