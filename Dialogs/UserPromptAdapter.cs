using Toybox.Studio.Project;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// Adapts the editor <see cref="Popups"/> to the asset layer's <see cref="IUserPrompt"/>, so asset operations
/// can report errors and ask for confirmation/rename input without depending on the dialog layer.
/// </summary>
public sealed class UserPromptAdapter : IUserPrompt
{
    public Task ShowErrorAsync(string title, string message) => Popups.ShowErrorAsync(title, message);

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText) =>
        Popups.ConfirmAsync(title, message, confirmText, cancelText);

    public Task<string?> PromptForTextAsync(string title, string watermark, string? initial, string confirmText) =>
        Popups.PromptForTextAsync(title, watermark, initial, confirmText: confirmText);
}
