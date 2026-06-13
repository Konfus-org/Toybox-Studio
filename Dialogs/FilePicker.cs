using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// Thin wrapper over the main window's storage provider for open-file dialogs.
/// </summary>
public sealed class FilePicker
{
    public async Task<string?> PickFileAsync(string title, params FilePickerFileType[] filters)
    {
        var provider = GetStorageProvider();
        if (provider is null)
            return null;

        var results = await provider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = filters,
            }).ContinueOnAnyContext();
        return results.Count > 0 ? results[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var provider = GetStorageProvider();
        if (provider is null)
            return null;

        var results = await provider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = title, AllowMultiple = false }).ContinueOnAnyContext();
        return results.Count > 0 ? results[0].TryGetLocalPath() : null;
    }

    private static IStorageProvider? GetStorageProvider()
    {
        var lifetime =
            Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        return lifetime?.MainWindow?.StorageProvider;
    }
}
