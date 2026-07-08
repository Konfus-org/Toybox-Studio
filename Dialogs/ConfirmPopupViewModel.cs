namespace Toybox.Studio.Dialogs;

/// <summary>
/// A question with one button per choice, resolving to the picked choice's INDEX (the
/// <see cref="Popups.ConfirmAsync{TChoice}"/> preset maps it back to the enum member). Buttons render
/// in reverse member order so the first member — the primary action — sits right-most, and the last
/// member is both the Escape target and the dismissal result.
/// </summary>
public sealed class ConfirmPopupViewModel : PopupViewModel<int>
{
    public ConfirmPopupViewModel(string title, string message, IReadOnlyList<string> choices)
        : base(title)
    {
        Message = message;

        // Dismissing the window (title-bar X, Escape) means the last choice — Cancel by convention.
        Result = choices.Count - 1;

        var buttons = new List<PopupButton>(choices.Count);
        for (var index = choices.Count - 1; index >= 0; index--)
            buttons.Add(Button(choices[index], index, isPrimary: index == 0, isCancel: index == choices.Count - 1));
        Buttons = buttons;
    }

    public string Message { get; }
}
