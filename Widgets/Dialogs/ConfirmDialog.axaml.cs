using System.Threading.Tasks;
using Avalonia.Controls;

namespace Toybox.Studio.Widgets.Dialogs;

/// <summary>
/// A modal yes/no dialog. Returns the user's choice (true = confirmed).
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public static async Task<bool> ShowAsync(
        Window owner,
        string title,
        string message,
        string confirmText,
        string cancelText)
    {
        var viewModel = new ConfirmDialogViewModel(title, message, confirmText, cancelText);
        var window = new ConfirmDialog { DataContext = viewModel };
        viewModel.CloseRequested += window.Close;
        await window.ShowDialog(owner);
        return viewModel.Confirmed;
    }
}
