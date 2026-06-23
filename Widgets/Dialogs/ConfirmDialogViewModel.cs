using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.Widgets.Dialogs;

/// <summary>
/// A modal yes/no question. <see cref="Confirmed"/> is true only when the user picks the confirm action;
/// cancelling or closing the dialog any other way leaves it false. Holds no
/// <see cref="Avalonia.Controls.Window"/> reference; it raises <see cref="CloseRequested"/> so the host
/// window closes itself.
/// </summary>
public sealed partial class ConfirmDialogViewModel : ObservableObject
{
    public ConfirmDialogViewModel(string title, string message, string confirmText, string cancelText)
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
    }

    /// <summary>Raised when the dialog should close.</summary>
    public event Action? CloseRequested;

    public string Title { get; }

    public string Message { get; }

    public string ConfirmText { get; }

    public string CancelText { get; }

    /// <summary>True once the user chose the confirm action.</summary>
    public bool Confirmed { get; private set; }

    [RelayCommand]
    private void Confirm()
    {
        Confirmed = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();
}
