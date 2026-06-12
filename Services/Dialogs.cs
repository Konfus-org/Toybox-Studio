using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Toybox.Studio.Services;

/// <summary>
/// Minimal modal message dialog, shown over the main window.
/// </summary>
public static class Dialogs
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
}
