using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Toybox.Studio.Dialogs;
using Toybox.Studio.Project;
using Toybox.Studio.PropertyGrid;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// A modal searchable asset chooser. Returns the user's <see cref="AssetPick"/> (select, clear, or cancel).
/// </summary>
public partial class AssetPickerDialog : Window
{
    public AssetPickerDialog()
    {
        InitializeComponent();
    }

    public static async Task<AssetPick> ShowAsync(
        Window owner,
        string title,
        IReadOnlyList<AssetMeta> options,
        ulong currentId)
    {
        var viewModel = new AssetPickerViewModel(title, options, currentId);
        var window = new AssetPickerDialog { DataContext = viewModel };
        viewModel.CloseRequested += window.Close;
        await window.ShowDialog(owner);
        return viewModel.Result;
    }
}
