using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Toybox.Studio.Services.Dialogs;

namespace Toybox.Studio.Widgets.Dialogs;

/// <summary>
/// A modal searchable type chooser (component types, script assets, …). Returns the chosen
/// <see cref="CatalogItem"/>, or null when the user cancels.
/// </summary>
public partial class CatalogPickerDialog : Window
{
    public CatalogPickerDialog()
    {
        InitializeComponent();
    }

    public static async Task<CatalogItem?> ShowAsync(
        Window owner,
        string title,
        string emptyMessage,
        IReadOnlyList<CatalogItem> options)
    {
        var viewModel = new CatalogPickerViewModel(title, emptyMessage, options);
        var window = new CatalogPickerDialog { DataContext = viewModel };
        viewModel.CloseRequested += window.Close;
        await window.ShowDialog(owner);
        return viewModel.Result;
    }
}
