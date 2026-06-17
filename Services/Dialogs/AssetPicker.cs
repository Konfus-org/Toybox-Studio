using Toybox.Studio.Utils;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Widgets.Ghost;

namespace Toybox.Studio.Services.Dialogs;

/// <summary>
/// Outcome of an <see cref="AssetPicker"/> dialog. <see cref="Confirmed"/> is false when the user
/// cancels (leave the reference untouched); when true, <see cref="Id"/> is the chosen asset id, or 0
/// when the user cleared the reference.
/// </summary>
public readonly record struct AssetPick(bool Confirmed, long Id);

/// <summary>
/// Minimal modal asset chooser: a searchable list of the assets matching a handle's type, shown over the
/// main window. Used by the handle picker to pick (or clear) a reference from the asset database.
/// </summary>
public static class AssetPicker
{
    public static async Task<AssetPick> ShowAsync(
        string title,
        IReadOnlyList<AssetEntry> options,
        long currentId)
    {
        var lifetime =
            Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var owner = lifetime?.MainWindow;
        if (owner is null)
            return new AssetPick(false, 0);

        var result = new AssetPick(false, 0);

        var search = new TextBox { PlaceholderText = "Search…" };
        var list = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            // Avalonia rebuilds the item template with a null item while recycling virtualized containers,
            // so the accessors must be null-safe — a bare asset.Name would NRE on the UI thread and take the
            // whole editor down during a layout pass.
            ItemTemplate = new FuncDataTemplate<AssetEntry>((asset, _) => new StackPanel
            {
                Margin = new Thickness(2, 3),
                Children =
                {
                    new TextBlock { Text = asset?.Name },
                    new TextBlock { Text = asset?.Type, Opacity = 0.55, FontSize = 11 },
                },
            }, supportsRecycling: false),
        };

        // Shown over the list whenever there's nothing to pick, so an empty picker reads as intentional
        // rather than broken. The message reflects whether the emptiness is from a search or a bare list.
        var ghost = new GhostView { IsHitTestVisible = false };

        void Refilter()
        {
            var query = search.Text?.Trim() ?? "";
            var filtered = query.Length == 0
                ? options
                : options
                    .Where(o => o.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            list.ItemsSource = filtered;

            var isEmpty = filtered.Count == 0;
            ghost.IsVisible = isEmpty;
            ghost.Message = query.Length == 0 ? "Nothing to pick here." : "No matches.";
        }

        Refilter();
        list.SelectedItem = options.FirstOrDefault(o => o.Id == currentId);
        search.TextChanged += (_, _) => Refilter();

        var clearButton = new Button { Content = "Clear", MinWidth = 72 };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 72 };
        var selectButton = new Button { Content = "Select", MinWidth = 72, IsDefault = true };

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 480,
            CanResize = true,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        void Confirm(AssetPick pick)
        {
            result = pick;
            dialog.Close();
        }

        selectButton.Click += (_, _) =>
        {
            if (list.SelectedItem is AssetEntry asset)
                Confirm(new AssetPick(true, asset.Id));
        };
        list.DoubleTapped += (_, _) =>
        {
            if (list.SelectedItem is AssetEntry asset)
                Confirm(new AssetPick(true, asset.Id));
        };
        clearButton.Click += (_, _) => Confirm(new AssetPick(true, 0));
        cancelButton.Click += (_, _) => dialog.Close();

        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(clearButton, Avalonia.Controls.Dock.Left);
        buttons.Children.Add(clearButton);
        buttons.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancelButton, selectButton },
        });

        var layout = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(search, Avalonia.Controls.Dock.Top);
        search.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);
        layout.Children.Add(search);
        layout.Children.Add(buttons);
        layout.Children.Add(new Border
        {
            BorderBrush = Avalonia.Media.Brush.Parse("#22FFFFFF"),
            BorderThickness = new Thickness(0.5),
            Child = new Panel { Children = { new ScrollViewer { Content = list }, ghost } },
        });

        dialog.Content = layout;
        await dialog.ShowDialog(owner).ContinueOnAnyContext();
        return result;
    }
}
