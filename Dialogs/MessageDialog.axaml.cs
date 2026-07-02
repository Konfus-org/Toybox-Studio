using System.Threading.Tasks;
using Avalonia.Controls;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// A one-button message dialog. Shows over <c>owner</c> when given (modal), otherwise standalone.
/// </summary>
public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    public static Task ShowAsync(
        Window? owner,
        string title,
        string message,
        // Fully-qualified: inside a Window subclass the bare `Icon` alias is shadowed by the inherited
        // Window.Icon property, so the alias must be spelled out here.
        IconPacks.Avalonia.Lucide.PackIconLucideKind iconName = IconPacks.Avalonia.Lucide.PackIconLucideKind.None,
        Avalonia.Media.Color? iconColor = null)
    {
        var viewModel = new MessageDialogViewModel(title, message, iconName, iconColor);
        var window = new MessageDialog { DataContext = viewModel };
        viewModel.CloseRequested += window.Close;

        if (owner is not null)
            return window.ShowDialog(owner);

        window.Show();
        return Task.CompletedTask;
    }
}
