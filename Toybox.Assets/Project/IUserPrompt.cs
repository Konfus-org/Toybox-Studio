namespace Toybox.Studio.Project;

/// <summary>
/// The user prompts asset operations raise — an error report, a delete/overwrite confirmation, a rename text
/// input — inverting the asset layer's dependency on the dialog layer. An adapter over the editor popups
/// supplies it; asset handles reach it through <see cref="AssetServices"/>.
/// </summary>
public interface IUserPrompt
{
    /// <summary>Shows a modal error with a title and message.</summary>
    Task ShowErrorAsync(string title, string message);

    /// <summary>Asks the user to confirm an action; true if they chose the confirm button.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText);

    /// <summary>Prompts for a line of text (seeded with <paramref name="initial"/>); null if cancelled.</summary>
    Task<string?> PromptForTextAsync(string title, string watermark, string? initial, string confirmText);
}
