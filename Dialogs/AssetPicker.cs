using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Toybox.Studio.Project;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// Opens the modal asset chooser from anywhere, resolving the owner (the main window) so callers don't have
/// to. A thin opener over the self-contained <see cref="AssetPickerDialog"/> in <c>Dialogs</c>.
/// </summary>
public static class AssetPicker
{
    public static async Task<AssetPick> ShowAsync(
        string title,
        IReadOnlyList<AssetMeta> options,
        ulong currentId)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        if (owner is null)
            return new AssetPick(false, 0);
        return await AssetPickerDialog.ShowAsync(owner, title, options, currentId).ContinueOnAnyContext();
    }
}
