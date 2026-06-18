using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Toybox.Studio.Utils;

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

    /// <summary>
    /// Shows the dialog and returns the user's choice. When <paramref name="dismiss"/> is signalled the
    /// dialog auto-closes (returning false) — used to retract a prompt whose reason no longer holds (e.g.
    /// an unresponsive engine that recovered on its own).
    /// </summary>
    public static async Task<bool> ShowAsync(
        Window owner,
        string title,
        string message,
        string confirmText,
        string cancelText,
        CancellationToken dismiss = default)
    {
        var viewModel = new ConfirmDialogViewModel(title, message, confirmText, cancelText);
        var window = new ConfirmDialog { DataContext = viewModel };
        viewModel.CloseRequested += window.Close;
        using var registration = dismiss.CanBeCanceled
            ? dismiss.Register(() => Dispatch.To(DispatchContext.UI, window.Close))
            : default;
        await window.ShowDialog(owner);
        return viewModel.Confirmed;
    }
}
