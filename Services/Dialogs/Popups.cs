using Toybox.Studio.Utils;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Toybox.Studio.Widgets.PropertyGrid;

namespace Toybox.Studio.Services.Dialogs;

/// <summary>
/// Minimal modal message dialog, shown over the main window.
/// </summary>
public static class Popups
{
    /// <summary>The app window icon, shown in every dialog's title bar.</summary>
    private static WindowIcon? AppIcon()
    {
        try
        {
            using var stream = Avalonia.Platform.AssetLoader.Open(
                new Uri("avares://Toybox.Studio/Assets/Icons/Toybox.png"));
            return new WindowIcon(stream);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A large Lucide content icon (e.g. an error or question glyph) for a dialog's header.</summary>
    private static IconView DialogIcon(string name, string color) => new()
    {
        IconName = name,
        IconColor = color,
        Width = 30,
        Height = 30,
        VerticalAlignment = VerticalAlignment.Top,
    };

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
            Icon = AppIcon(),
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
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 14,
                        Children =
                        {
                            DialogIcon("CircleAlert", "RED"),
                            new TextBlock
                            {
                                Text = message, TextWrapping = TextWrapping.Wrap,
                                VerticalAlignment = VerticalAlignment.Center, MaxWidth = 340,
                            },
                        },
                    },
                    okButton,
                },
            },
        };

        okButton.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner).ContinueOnAnyContext();
    }

    /// <summary>
    /// Prompts for a new entity: a name field and an "Add as global" checkbox. Returns the entered name
    /// (trimmed) and global flag, or null if the user cancelled or dismissed the dialog.
    /// </summary>
    public static async Task<(string Name, bool IsGlobal)?> ShowAddEntityAsync(Window? owner = null)
    {
        var lifetime =
            Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        owner ??= lifetime?.MainWindow;
        if (owner is null)
            return null;

        var nameBox = new TextBox { PlaceholderText = "Entity name", MinWidth = 280 };
        var globalCheck = new CheckBox { Content = "Global", Margin = new Thickness(0, 4, 0, 0) };

        var addButton = new Button { Content = "Add", MinWidth = 80, IsDefault = true };
        addButton.Classes.Add("action");
        var cancelButton = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };

        (string Name, bool IsGlobal)? result = null;

        var dialog = new Window
        {
            Title = "Add entity",
            Icon = AppIcon(),
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
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 14,
                        Children =
                        {
                            DialogIcon("CirclePlus", "GREEN"),
                            new StackPanel
                            {
                                Spacing = 8,
                                VerticalAlignment = VerticalAlignment.Center,
                                Children = { nameBox, globalCheck },
                            },
                        },
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, addButton },
                    },
                },
            },
        };

        addButton.Click += (_, _) =>
        {
            result = (nameBox.Text?.Trim() ?? "", globalCheck.IsChecked == true);
            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();
        dialog.Opened += (_, _) => nameBox.Focus();

        await dialog.ShowDialog(owner).ContinueOnAnyContext();
        return result;
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
        confirmButton.Classes.Add("action");
        var cancelButton = new Button { Content = cancelText, MinWidth = 80, IsCancel = true };

        var dialog = new Window
        {
            Title = title,
            Icon = AppIcon(),
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
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 14,
                        Children =
                        {
                            DialogIcon("CircleHelp", "BLUE"),
                            new TextBlock
                            {
                                Text = message, TextWrapping = TextWrapping.Wrap,
                                VerticalAlignment = VerticalAlignment.Center, MaxWidth = 340,
                            },
                        },
                    },
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
