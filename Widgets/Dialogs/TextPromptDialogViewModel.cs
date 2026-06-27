using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.Widgets.Dialogs;

/// <summary>
/// Backs a single-line text prompt (e.g. "name this layout"): one text field plus OK / Cancel.
/// <see cref="Confirmed"/> is true only when the user commits with OK; cancelling or closing leaves it false.
/// When <see cref="CanBeEmpty"/> is false the OK command stays disabled until the field has non-whitespace
/// text. Holds no <see cref="Avalonia.Controls.Window"/> reference; it raises <see cref="CloseRequested"/> so
/// the host window closes itself.
/// </summary>
public sealed partial class TextPromptDialogViewModel : ObservableObject
{
    public TextPromptDialogViewModel(
        string title, string watermark, string? initial, bool canBeEmpty, string confirmText)
    {
        Title = title;
        Watermark = watermark;
        Value = initial ?? "";
        CanBeEmpty = canBeEmpty;
        ConfirmText = confirmText;
    }

    /// <summary>Raised when the dialog should close.</summary>
    public event Action? CloseRequested;

    public string Title { get; }

    public string Watermark { get; }

    public string ConfirmText { get; }

    public bool CanBeEmpty { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string Value { get; set; }

    /// <summary>True once the user committed with OK.</summary>
    public bool Confirmed { get; private set; }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Confirmed = true;
        CloseRequested?.Invoke();
    }

    private bool CanConfirm() => CanBeEmpty || !string.IsNullOrWhiteSpace(Value);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();
}
