using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services.Dialogs;

namespace Toybox.Studio.Widgets.Dialogs;

/// <summary>
/// A modal Save / Don't Save / Cancel prompt for unsaved changes. <see cref="Choice"/> defaults to
/// <see cref="SaveChoice.Cancel"/>, so closing the dialog any other way keeps things open. Raises
/// <see cref="CloseRequested"/> so the host window closes itself.
/// </summary>
public sealed partial class SaveChangesDialogViewModel : ObservableObject
{
    public SaveChangesDialogViewModel(string title, string message)
    {
        Title = title;
        Message = message;
    }

    /// <summary>Raised when the dialog should close.</summary>
    public event Action? CloseRequested;

    public string Title { get; }

    public string Message { get; }

    /// <summary>The user's choice; <see cref="SaveChoice.Cancel"/> until they pick Save or Don't Save.</summary>
    public SaveChoice Choice { get; private set; } = SaveChoice.Cancel;

    [RelayCommand]
    private void Save()
    {
        Choice = SaveChoice.Save;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Discard()
    {
        Choice = SaveChoice.Discard;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Choice = SaveChoice.Cancel;
        CloseRequested?.Invoke();
    }
}
