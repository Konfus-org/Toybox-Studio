using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// Adapts the editor <see cref="Popups"/> to the engine layer's <see cref="IEnginePrompt"/>, so the core
/// engine watchdog can ask the user to force-restart a frozen engine without depending on the dialog layer.
/// </summary>
public sealed class EnginePromptAdapter : IEnginePrompt
{
    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        string cancelText,
        CancellationToken dismiss) =>
        Popups.ConfirmAsync(title, message, confirmText, cancelText, dismiss: dismiss);
}
