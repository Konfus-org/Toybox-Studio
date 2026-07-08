using System.Windows.Input;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// One action button in a popup's shared button row (rendered by <see cref="PopupWindow"/>).
/// <see cref="IsPrimary"/> styles it as the action button and makes it the Enter default;
/// <see cref="IsCancel"/> makes it the Escape target.
/// </summary>
public sealed record PopupButton(string Label, ICommand Command, bool IsPrimary = false, bool IsCancel = false);
