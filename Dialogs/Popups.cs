using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Extensions;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// Opens the app's modal popups, resolving the owner (the main window) so callers don't have to. The
/// presets cover the common shapes — message severities and enum-driven confirms — and
/// <see cref="ShowAsync"/> hosts any custom <see cref="PopupViewModel{TResult}"/> (text prompt, list
/// pick, property-grid form, …) in the shared <see cref="PopupWindow"/> chrome.
/// </summary>
public sealed class Popups
{
    /// <summary>Shows a plain informational message with an OK button.</summary>
    public Task InfoAsync(string title, string message) =>
        ShowAsync(new MessagePopupViewModel(title, message, MessageSeverity.Info));

    /// <summary>Shows a warning message with an OK button.</summary>
    public Task WarningAsync(string title, string message) =>
        ShowAsync(new MessagePopupViewModel(title, message, MessageSeverity.Warning));

    /// <summary>Shows an error message with an alert icon and an OK button.</summary>
    public Task ErrorAsync(string title, string message) =>
        ShowAsync(new MessagePopupViewModel(title, message, MessageSeverity.Error));

    /// <summary>Asks a Yes / No / Cancel question and returns the choice (Cancel on dismissal).</summary>
    public Task<Confirmation> ConfirmAsync(string title, string message) =>
        ConfirmAsync<Confirmation>(title, message);

    /// <summary>
    /// Asks a question whose answers are <typeparamref name="TChoice"/>'s members, one button each —
    /// labeled by the member's <see cref="Utils.Attributes.DisplayNameAttribute"/> when present,
    /// else its humanized name. By convention the FIRST member is the primary (affirmative) action and
    /// the LAST is the cancel: dismissing the popup (title-bar X, Escape) returns the last member.
    /// </summary>
    public async Task<TChoice> ConfirmAsync<TChoice>(string title, string message)
        where TChoice : struct, Enum
    {
        var choices = Enum.GetValues<TChoice>();
        var labels = choices.Select(choice => choice.GetDisplayName()).ToList();
        var popup = new ConfirmPopupViewModel(title, message, labels);
        return choices[await ShowAsync(popup).ContinueOnSameContext()];
    }

    /// <summary>
    /// Hosts any popup view-model modally over the main window and returns its result. With no main
    /// window yet (startup/teardown) the popup can't show and resolves as dismissed.
    /// </summary>
    public async Task<TResult?> ShowAsync<TResult>(PopupViewModel<TResult> popup)
    {
        if (MainWindow() is not { } owner)
            return popup.Result;

        var window = new PopupWindow { DataContext = popup };
        popup.CloseRequested += window.Close;
        await window.ShowDialog(owner).ContinueOnSameContext();
        return popup.Result;
    }

    /// <summary>The main window, which owns every popup so it centres and stays modal over the editor.</summary>
    private static Window? MainWindow() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
