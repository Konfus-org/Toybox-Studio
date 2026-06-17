using Toybox.Studio.Utils;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Toybox.Studio.Widgets.Dialogs;

namespace Toybox.Studio.Services.Dialogs;

/// <summary>
/// Opens the app's modal dialogs from anywhere, resolving the owner (the main window) so callers don't have
/// to. Each method is a thin opener over a self-contained MVVM dialog in <c>Widgets/Dialogs</c>.
/// </summary>
public static class Popups
{
    /// <summary>The main window, which owns every dialog so it centres and stays modal over the editor.</summary>
    private static Window? MainWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>Shows an error message with an alert icon and an OK button.</summary>
    public static async Task ShowErrorAsync(string title, string message)
    {
        if (MainWindow() is not { } owner)
            return;
        await MessageDialog.ShowAsync(owner, title, message, "CircleAlert", "RED").ContinueOnAnyContext();
    }

    /// <summary>
    /// Shows a plain informational message with an OK button. Owned by <paramref name="owner"/> when given,
    /// else the main window; shown standalone if neither exists yet (e.g. during startup).
    /// </summary>
    public static Task ShowMessageAsync(string title, string message, Window? owner = null) =>
        MessageDialog.ShowAsync(owner ?? MainWindow(), title, message);

    /// <summary>
    /// Prompts for a new entity: a name field and an "add as global" checkbox. Returns the entered name
    /// (trimmed) and global flag, or null if the user cancelled or dismissed the dialog.
    /// </summary>
    public static async Task<(string Name, bool IsGlobal)?> ShowAddEntityAsync(Window? owner = null)
    {
        owner ??= MainWindow();
        if (owner is null)
            return null;
        return await AddEntityDialog.ShowAsync(owner).ContinueOnAnyContext();
    }

    /// <summary>
    /// Shows a modal yes/no question and returns the user's choice (true = confirmed). Owned by
    /// <paramref name="owner"/> when given, else the main window — pass the asking window when prompting from
    /// inside another dialog so it nests correctly.
    /// </summary>
    public static async Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText = "Yes",
        string cancelText = "No",
        Window? owner = null)
    {
        owner ??= MainWindow();
        if (owner is null)
            return false;
        return await ConfirmDialog.ShowAsync(owner, title, message, confirmText, cancelText)
            .ContinueOnAnyContext();
    }

    /// <summary>
    /// Shows a Save / Don't Save / Cancel prompt for the named unsaved items and returns the choice. Used for
    /// both a single closing panel and the consolidated app-close prompt. With no owner window (e.g. during
    /// teardown) it returns <see cref="SaveChoice.Discard"/> so it never blocks shutdown.
    /// </summary>
    public static async Task<SaveChoice> ShowSaveChangesAsync(
        IReadOnlyList<string> names,
        Window? owner = null)
    {
        owner ??= MainWindow();
        if (owner is null)
            return SaveChoice.Discard;

        var list = string.Join(", ", names);
        var message = names.Count == 1
            ? $"Save changes to {list} before closing?"
            : $"Save changes to the following before closing?\n\n{list}";
        return await SaveChangesDialog.ShowAsync(owner, "Unsaved changes", message).ContinueOnAnyContext();
    }
}
