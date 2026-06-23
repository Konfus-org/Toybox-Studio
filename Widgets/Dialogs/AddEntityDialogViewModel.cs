using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.Widgets.Dialogs;

/// <summary>
/// Prompts for a new entity: a name and an "add as global" flag. <see cref="Confirmed"/> is true only when
/// the user commits with Add; cancelling or closing the dialog leaves it false. Holds no
/// <see cref="Avalonia.Controls.Window"/> reference; it raises <see cref="CloseRequested"/> so the host
/// window closes itself.
/// </summary>
public sealed partial class AddEntityDialogViewModel : ObservableObject
{
    /// <summary>Raised when the dialog should close.</summary>
    public event Action? CloseRequested;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial bool IsGlobal { get; set; }

    /// <summary>True once the user committed with Add.</summary>
    public bool Confirmed { get; private set; }

    [RelayCommand]
    private void Add()
    {
        Confirmed = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();
}
