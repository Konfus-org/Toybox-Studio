using System.Threading.Tasks;
using Avalonia.Controls;

namespace Toybox.Studio.Widgets.Dialogs;

/// <summary>
/// Prompts for a new entity. Returns the trimmed name and global flag, or null if the user cancelled or
/// dismissed the dialog.
/// </summary>
public partial class AddEntityDialog : Window
{
    public AddEntityDialog()
    {
        InitializeComponent();
        // Land the caret in the name field so the user can type immediately.
        Opened += (_, _) => NameBox.Focus();
    }

    public static async Task<(string Name, bool IsGlobal)?> ShowAsync(Window owner)
    {
        var viewModel = new AddEntityDialogViewModel();
        var window = new AddEntityDialog { DataContext = viewModel };
        viewModel.CloseRequested += window.Close;
        await window.ShowDialog(owner);
        return viewModel.Confirmed
            ? (viewModel.Name.Trim(), viewModel.IsGlobal)
            : null;
    }
}
