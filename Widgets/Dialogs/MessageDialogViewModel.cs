using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.Widgets.Dialogs;

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
        string? iconName = null,
        string? iconColor = null,
        string okText = "OK")
    {
        Title = title;
        Message = message;
        IconName = iconName;
        IconColor = iconColor;
        OkText = okText;
    }

    public string Title { get; }

    public string Message { get; }

    /// <summary>Lucide icon name for the header glyph, or null for a plain (icon-less) message.</summary>
    public string? IconName { get; }

    /// <summary>A tbx <c>Color</c> constant name (e.g. "RED") tinting the header glyph.</summary>
    public string? IconColor { get; }

    public string OkText { get; }

    /// <summary>Raised when the dialog should close.</summary>
    public event Action? CloseRequested;

    [RelayCommand]
    private void Ok() => CloseRequested?.Invoke();
}
