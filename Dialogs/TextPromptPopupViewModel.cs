using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// A single-line text prompt (e.g. naming a layout): one text field plus confirm / Cancel, resolving
/// to the trimmed text or null on cancel/dismiss. With <see cref="CanBeEmpty"/> false the confirm
/// button stays disabled until the field has non-whitespace text.
/// </summary>
public sealed partial class TextPromptPopupViewModel : PopupViewModel<string?>
{
    public TextPromptPopupViewModel(
        string title,
        string watermark = "",
        string? initial = null,
        bool canBeEmpty = false,
        string confirmText = "OK")
        : base(title)
    {
        Watermark = watermark;
        Value = initial ?? "";
        CanBeEmpty = canBeEmpty;
        Buttons =
        [
            new PopupButton("Cancel", CancelCommand, IsCancel: true),
            new PopupButton(confirmText, ConfirmCommand, IsPrimary: true),
        ];
    }

    public string Watermark { get; }

    public bool CanBeEmpty { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string Value { get; set; }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => Complete(Value.Trim());

    private bool CanConfirm() => CanBeEmpty || !string.IsNullOrWhiteSpace(Value);

    [RelayCommand]
    private void Cancel() => Complete(null);
}
