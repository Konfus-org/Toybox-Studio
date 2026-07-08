using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// The unsaved-changes choice set (Save is the primary action, Cancel the dismissal — see
/// <see cref="Popups.ConfirmAsync{TChoice}"/>'s member-order convention).
/// </summary>
public enum SaveChoice
{
    Save,

    [DisplayName("Don't Save")]
    DontSave,

    Cancel,
}
