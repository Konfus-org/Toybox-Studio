namespace Toybox.Studio.EngineApi;

/// <summary>
/// The user confirmation the engine watchdog needs when the engine wedges (force-restart vs. keep waiting),
/// inverting the engine layer's dependency on the dialog layer. An adapter over the editor popups supplies
/// it; the prompt is retracted if <paramref name="dismiss"/> fires (the engine recovered).
/// </summary>
public interface IEnginePrompt
{
    Task<bool> ConfirmAsync(
        string title,
        string message,
        string confirmText,
        string cancelText,
        CancellationToken dismiss);
}
