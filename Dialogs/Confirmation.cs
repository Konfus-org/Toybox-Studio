namespace Toybox.Studio.Dialogs;

/// <summary>
/// The default confirm choice set. Member order matters to <see cref="Popups.ConfirmAsync{TChoice}"/>:
/// the first member is the primary action, the last is the cancel (and the dismissal value).
/// </summary>
public enum Confirmation
{
    Yes,
    No,
    Cancel,
}
