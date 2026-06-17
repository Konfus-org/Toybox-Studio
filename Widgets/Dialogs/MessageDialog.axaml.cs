using System.Threading.Tasks;
using Avalonia.Controls;

namespace Toybox.Studio.Widgets.Dialogs;

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
        string? iconName = null,
        string? iconColor = null)
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
