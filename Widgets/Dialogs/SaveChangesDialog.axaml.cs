using System.Threading.Tasks;
using Avalonia.Controls;
using Toybox.Studio.Services.Dialogs;

namespace Toybox.Studio.Widgets.Dialogs;

/// <summary>
/// A modal Save / Don't Save / Cancel dialog. Returns the user's <see cref="SaveChoice"/>.
/// </summary>
public partial class SaveChangesDialog : Window
{
    public SaveChangesDialog()
    {
        InitializeComponent();
    }

    public static async Task<SaveChoice> ShowAsync(Window owner, string title, string message)
    {
        var viewModel = new SaveChangesDialogViewModel(title, message);
        var window = new SaveChangesDialog { DataContext = viewModel };
        viewModel.CloseRequested += window.Close;
        await window.ShowDialog(owner);
        return viewModel.Choice;
    }
}
