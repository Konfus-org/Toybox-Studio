using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// Minimal modal message dialog, shown over the main window.
/// </summary>
public static class Popups
{
    public static async Task ShowErrorAsync(string title, string message)
    {
        var lifetime =
            Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var owner = lifetime?.MainWindow;
        if (owner is null)
            return;

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 80,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var dialog = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    okButton,
                },
            },
        };

        okButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner).ContinueOnAnyContext();
    }

    /// <summary>
    /// Shows a modal yes/no question and returns the user's choice (true = confirmed). Closing the dialog
    /// any other way counts as not confirmed. Owned by <paramref name="owner"/> when given, else the main
    /// window — pass the asking window when prompting from inside another dialog so it nests correctly.
    /// </summary>
    public static async Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "Yes",
        string cancelText = "No",
        Window? owner = null)
    {
        var lifetime =
            Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        owner ??= lifetime?.MainWindow;
        if (owner is null)
            return false;

        var confirmed = false;
        var confirmButton = new Button { Content = confirmText, MinWidth = 80, IsDefault = true };
        var cancelButton = new Button { Content = cancelText, MinWidth = 80, IsCancel = true };

        var dialog = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, confirmButton },
                    },
                },
            },
        };

        confirmButton.Click += (_, _) =>
        {
            confirmed = true;
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(owner).ContinueOnAnyContext();
        return confirmed;
    }
}
