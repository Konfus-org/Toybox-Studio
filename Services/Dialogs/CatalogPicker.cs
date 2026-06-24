using Toybox.Studio.Utils;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Toybox.Studio.Widgets.Dialogs;

namespace Toybox.Studio.Services.Dialogs;

/// <summary>
/// One pickable entry in a <see cref="CatalogPicker"/>: an opaque <see cref="Key"/> the caller acts on (a
/// component wire name, a script asset id, …), a humanised <see cref="Title"/> and <see cref="Subtitle"/>,
/// and an optional [[tbx::icon]]-style badge.
/// </summary>
public sealed record CatalogItem(
    string Key,
    string Title,
    string Subtitle,
    string? Icon = null,
    string? IconColor = null);

/// <summary>
/// Opens the modal "choose a type" chooser from anywhere, resolving the owner (the main window) so callers
/// don't have to. A thin opener over the self-contained <see cref="CatalogPickerDialog"/>. Returns the chosen
/// item, or null when the user cancels.
/// </summary>
public static class CatalogPicker
{
    public static async Task<CatalogItem?> ShowAsync(
        string title,
        string emptyMessage,
        IReadOnlyList<CatalogItem> options)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        if (owner is null)
            return null;
        return await CatalogPickerDialog.ShowAsync(owner, title, emptyMessage, options)
            .ContinueOnAnyContext();
    }
}
