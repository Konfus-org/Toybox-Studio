using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// A one-button informational dialog: an optional header icon, a wrapped message, and an OK button.
/// Doubles as the error popup (seed it with an alert icon) and the plain message box (no icon). Holds no
/// <see cref="Avalonia.Controls.Window"/> reference; it raises <see cref="CloseRequested"/> so the host
/// window closes itself.
/// </summary>
public sealed partial class MessageDialogViewModel : ObservableObject
{
    public MessageDialogViewModel(
        string title,
        string message,
        Icon iconName = Icon.None,
        Avalonia.Media.Color? iconColor = null,
        string okText = "OK")
    {
        Title = title;
        Message = message;
        IconName = iconName;
        IconColor = iconColor;
        OkText = okText;
    }

    /// <summary>Raised when the dialog should close.</summary>
    public event Action? CloseRequested;

    public string Title { get; }

    public string Message { get; }

    /// <summary>Lucide icon for the header glyph, or <see cref="Icon.None"/> for a plain message.</summary>
    public Icon IconName { get; }

    /// <summary>Palette colour tinting the header glyph, or null to inherit.</summary>
    public Avalonia.Media.Color? IconColor { get; }

    public string OkText { get; }

    [RelayCommand]
    private void Ok() => CloseRequested?.Invoke();
}
