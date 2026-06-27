using System.Threading.Tasks;
using Avalonia.Controls;

namespace Toybox.Studio.Widgets.Dialogs;

/// <summary>
/// A single-line text prompt with OK / Cancel. Returns the entered text (trimmed), or null if the user
/// cancelled or dismissed the dialog.
/// </summary>
public partial class TextPromptDialog : Window
{
    public TextPromptDialog()
    {
        InitializeComponent();
        // Land the caret in the field (with any seeded text selected) so the user can type or overwrite at once.
        Opened += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public static async Task<string?> ShowAsync(
        Window owner, string title, string watermark, string? initial, bool canBeEmpty, string confirmText)
    {
        var viewModel = new TextPromptDialogViewModel(title, watermark, initial, canBeEmpty, confirmText);
        var window = new TextPromptDialog { DataContext = viewModel };
        viewModel.CloseRequested += window.Close;
        await window.ShowDialog(owner);
        return viewModel.Confirmed ? viewModel.Value.Trim() : null;
    }
}
