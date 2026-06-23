using Toybox.Studio.Utils;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Widgets.Dialogs;

namespace Toybox.Studio.Services.Dialogs;

/// <summary>
/// Outcome of an <see cref="AssetPicker"/> dialog. <see cref="Confirmed"/> is false when the user
/// cancels (leave the reference untouched); when true, <see cref="Id"/> is the chosen asset id, or 0
/// when the user cleared the reference.
/// </summary>
public readonly record struct AssetPick(bool Confirmed, long Id);

/// <summary>
/// Opens the modal asset chooser from anywhere, resolving the owner (the main window) so callers don't have
/// to. A thin opener over the self-contained <see cref="AssetPickerDialog"/> in <c>Widgets/Dialogs</c>.
/// </summary>
public static class AssetPicker
{
    public static async Task<AssetPick> ShowAsync(
        string title,
        IReadOnlyList<Asset> options,
        long currentId)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        if (owner is null)
            return new AssetPick(false, 0);
        return await AssetPickerDialog.ShowAsync(owner, title, options, currentId).ContinueOnAnyContext();
    }
}
