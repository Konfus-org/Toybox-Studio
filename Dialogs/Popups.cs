using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia;
using Toybox.Studio.Utils.Composition;
using Toybox.Studio.Utils.Extensions;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// Opens the app's modal popups, resolving the owner (the main window) so callers don't have to. The
/// presets cover the common shapes — message severities and enum-driven confirms — and
/// <see cref="ShowAsync"/> hosts any custom <see cref="PopupViewModel{TResult}"/> (text prompt, list
/// pick, property-grid form, …) in the shared <see cref="PopupWindow"/> chrome.
/// </summary>
public sealed class Popups(ViewModelFactory viewModels)
{
    /// <summary>Shows a plain informational message with an OK button.</summary>
    public Task InfoAsync(string title, string message, Window? owner = null) =>
        ShowAsync(viewModels.Create<MessagePopupViewModel>(title, message, MessageSeverity.Info), owner);

    /// <summary>Shows a warning message with an OK button.</summary>
    public Task WarningAsync(string title, string message, Window? owner = null) =>
        ShowAsync(viewModels.Create<MessagePopupViewModel>(title, message, MessageSeverity.Warning), owner);

    /// <summary>Shows an error message with an alert icon and an OK button. Hosts over the main window,
    /// or over <paramref name="owner"/> when given (the startup splash, before the main window exists).</summary>
    public Task ErrorAsync(string title, string message, Window? owner = null) =>
        ShowAsync(viewModels.Create<MessagePopupViewModel>(title, message, MessageSeverity.Error), owner);

    /// <summary>Asks a Yes / No / Cancel question and returns the choice (Cancel on dismissal). Hosts
    /// over the main window, or over <paramref name="owner"/> when given (the startup splash, before the
    /// main window exists).</summary>
    public Task<Confirmation> ConfirmAsync(string title, string message, Window? owner = null) =>
        ConfirmAsync<Confirmation>(title, message, owner);

    /// <summary>
    /// Asks a question whose answers are <typeparamref name="TChoice"/>'s members, one button each —
    /// labeled by the member's <see cref="Utils.Attributes.DisplayNameAttribute"/> when present,
    /// else its humanized name. By convention the FIRST member is the primary (affirmative) action and
    /// the LAST is the cancel: dismissing the popup (title-bar X, Escape) returns the last member.
    /// </summary>
    public async Task<TChoice> ConfirmAsync<TChoice>(string title, string message, Window? owner = null)
        where TChoice : struct, Enum
    {
        var choices = Enum.GetValues<TChoice>();
        var labels = choices.Select(choice => choice.GetDisplayName()).ToList();
        var popup = viewModels.Create<ConfirmPopupViewModel>(title, message, labels);
        return choices[await ShowAsync(popup, owner).ContinueOnSameContext()];
    }

    /// <summary>
    /// Hosts any popup view-model modally over the main window (or over <paramref name="owner"/> when
    /// given — the startup splash, before the main window exists) and returns its result. With no window
    /// to own it (startup/teardown) the popup can't show and resolves as dismissed.
    /// </summary>
    public async Task<TResult?> ShowAsync<TResult>(PopupViewModel<TResult> popup, Window? owner = null)
    {
        if ((owner ?? MainWindow()) is not { } host)
            return popup.Result;

        var window = new PopupWindow { DataContext = popup };
        popup.CloseRequested += window.Close;
        await window.ShowDialog(host).ContinueOnSameContext();
        return popup.Result;
    }

    /// <summary>
    /// Opens the OS folder picker over the main window and returns the chosen folder's local path (null on
    /// cancel, on a non-local folder, or when there is no main window). Used by File ▸ Open ▸ Project.
    /// </summary>
    public async Task<string?> PickFolderAsync(string title)
    {
        if (MainWindow() is not { StorageProvider: { } storage })
            return null;

        var folders = await storage
            .OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false })
            .ContinueOnSameContext();
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// Opens the OS file picker over the main window, filtered to <paramref name="extensions"/> (each a bare
    /// extension without the dot, e.g. <c>"cpp"</c>), and returns the chosen file's local path (null on cancel,
    /// on a non-local file, or when there is no main window). Used by File ▸ Open ▸ Source.
    /// </summary>
    public async Task<string?> PickFileAsync(string title, string filterName, params string[] extensions)
    {
        if (MainWindow() is not { StorageProvider: { } storage })
            return null;

        var files = await storage
            .OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType(filterName) { Patterns = [.. extensions.Select(e => "*." + e)] }],
            })
            .ContinueOnSameContext();
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>The main window, which owns every popup so it centres and stays modal over the editor.</summary>
    private static Window? MainWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
